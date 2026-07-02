
## 1. PROJECT VISION
**Atlas** is a self-hostable, distributed background job & workflow orchestration platform — similar in spirit to Hangfire/Sidekiq/Temporal — that lets teams:
- Enqueue, schedule (cron), and retry background jobs reliably across multiple worker nodes.
- Monitor job execution in real time via a live dashboard.
- Define multi-step workflows (job chains / DAGs), not just single fire-and-forget jobs.
- Operate it via REST API, CLI, or Dashboard UI.
- Deploy it in a distributed, horizontally-scalable fashion (multiple worker nodes pulling from a shared queue).

Target users: platform/backend teams who need a self-hosted alternative to cloud job schedulers, with full visibility and control.

---

## 2. TECH STACK (opinionated — follow unless a strong reason not to)

| Layer | Technology |
|---|---|
| Backend API / Core | ASP.NET Core 8 (Minimal APIs + Controllers where needed) |
| Dashboard Frontend | React + TypeScript + Vite (preferred over Blazor for richer real-time UX; use Blazor Server only if the user explicitly wants a pure .NET stack) |
| Real-time updates | SignalR (job status, live logs, worker heartbeat) |
| Persistence | PostgreSQL (jobs, workflows, users, audit logs) via EF Core |
| Queue / Broker | Redis (as the fast job queue + pub/sub) — Postgres as source of truth, Redis as the hot queue |
| Cron Scheduling | Custom scheduler service using Cronos (cron expression parsing) + a leader-election lock (Postgres advisory lock or Redis lock) to avoid duplicate scheduling across nodes |
| Auth | ASP.NET Core Identity + JWT (dashboard/API) + API Keys (for CLI/service-to-service) |
| Metrics | OpenTelemetry + Prometheus exporter + Grafana-ready dashboards |
| CLI | .NET Tool (`dotnet tool install -g atlas-cli`) built with System.CommandLine |
| Containerization | Docker + docker-compose for local dev (Postgres, Redis, API, Worker, Dashboard) |
| Testing | xUnit + Testcontainers (real Postgres/Redis in tests, not mocks, for integration tests) |

---

## 3. HIGH-LEVEL ARCHITECTURE

```
                         ┌─────────────────────┐
                         │   Dashboard (React)  │
                         │  REST + SignalR conn │
                         └──────────┬───────────┘
                                    │
                         ┌──────────▼───────────┐
                         │   Atlas.Api (ASP.NET) │
                         │  REST API + SignalR   │
                         │  Hub + AuthN/AuthZ     │
                         └──────────┬───────────┘
                                    │
              ┌─────────────────────┼─────────────────────┐
              │                     │                     │
     ┌────────▼───────┐   ┌────────▼────────┐   ┌────────▼────────┐
     │  PostgreSQL      │   │   Redis Queue    │   │  Scheduler Svc   │
     │  (source of truth│   │  (hot job queue, │   │  (cron ticks,    │
     │  jobs/workflows/  │   │   pub/sub events)│   │   leader-elected)│
     │  users/logs)      │   └────────┬────────┘   └────────┬────────┘
     └──────────────────┘            │                     │
                                      │                     │
                        ┌─────────────▼─────────────────────▼───────┐
                        │        Worker Nodes (Atlas.Worker)         │
                        │  N horizontally-scaled instances, each:    │
                        │   - polls/subscribes to Redis queue        │
                        │   - executes job handlers                  │
                        │   - reports heartbeat + status back        │
                        │   - streams logs via SignalR/Redis pub-sub │
                        └─────────────────────────────────────────────┘
```

Key architectural decisions to implement:
1. **Postgres is the source of truth** for job state (Created → Enqueued → Processing → Succeeded/Failed/Retrying → Deleted). Redis is only the fast dispatch layer — if Redis is lost, jobs are recoverable from Postgres.
2. **At-least-once delivery.** Workers must claim a job atomically (e.g., `SELECT ... FOR UPDATE SKIP LOCKED` or a Redis `BRPOPLPUSH`/reliable-queue pattern) so two workers never process the same job simultaneously.
3. **Worker heartbeats** every N seconds; a background "reaper" requeues jobs from workers that stop heartbeating (crash recovery).
4. **Idempotency keys** supported on job submission to prevent duplicate enqueues from client retries.

---

## 4. CORE FEATURE SPEC

### 4.1 Job Queue
- Job = `{ Id, Type, PayloadJson, Priority, Status, Attempts, MaxAttempts, ScheduledAt, CreatedAt, Queue }`
- Support multiple **named queues** (e.g., `default`, `emails`, `reports`) with configurable per-queue concurrency limits per worker.
- Priority levels within a queue (Low/Normal/High/Critical).
- FIFO within same priority.

### 4.2 Worker Nodes
- Workers register themselves on startup (`WorkerId`, host, PID, supported job types, concurrency capacity).
- Workers poll assigned queues, execute jobs inside a job handler registry (`IJobHandler<T>` pattern — user registers handlers by job type).
- Graceful shutdown: stop pulling new jobs, finish in-flight jobs, deregister.
- Worker pool concurrency is configurable (e.g., max 10 concurrent jobs per node).

### 4.3 Retries
- Exponential backoff with jitter, configurable per job type (`baseDelay`, `maxDelay`, `maxAttempts`).
- Dead-letter queue after max attempts exhausted — visible and manually re-triggerable from dashboard.
- Distinguish **transient failures** (retry) vs **permanent failures** (fail fast, no retry) via exception typing.

### 4.4 Cron Scheduler
- Cron expressions (standard 5/6-field via Cronos) stored as `ScheduledJob` definitions.
- Leader-election (single active scheduler instance across nodes, via Postgres advisory lock) ticks every minute, evaluates due schedules, enqueues jobs.
- Misfire policy configurable (skip vs run-once-immediately if missed while scheduler was down).
- Support "run now" manual trigger of any scheduled job from dashboard/CLI.

### 4.5 Workflows (bonus but important — mention this even though not in original bullet list, as a senior engineer would suggest it)
- Support simple job **chains** (Job B runs only after Job A succeeds) and basic DAGs.
- Store as `WorkflowDefinition` with steps + dependencies; `WorkflowRun` tracks execution state per step.

### 4.6 Live Logs (SignalR)
- Each job execution streams structured log lines (`timestamp, level, message`) to a SignalR hub group scoped to that JobId.
- Logs also persisted (Postgres or cheap log store) for post-hoc viewing, not just live tail.
- Dashboard "Job Detail" page subscribes to the hub and tails logs in real time, with a scrollback buffer.

### 4.7 Metrics
- Expose `/metrics` (Prometheus format) with: jobs enqueued/sec, jobs succeeded/failed, queue depth per queue, worker count, average job duration (histogram), retry rate.
- Dashboard "Overview" page renders these as charts (recharts or similar) polling or via SignalR push.

### 4.8 Authentication
- Dashboard: username/password + JWT session, ASP.NET Core Identity, role-based (`Admin`, `Operator`, `Viewer`).
- API/CLI: API Key auth (hashed at rest, scoped permissions).
- All mutating endpoints require `Operator`+; read-only endpoints allow `Viewer`.

### 4.9 REST API
- `POST /api/jobs` — enqueue a job
- `GET /api/jobs/{id}` — job detail + status history
- `GET /api/jobs?queue=&status=&page=` — list/filter
- `POST /api/jobs/{id}/retry` — manual retry
- `DELETE /api/jobs/{id}` — cancel/delete
- `POST /api/schedules` / `GET /api/schedules` — manage cron schedules
- `GET /api/workers` — list active workers + health
- `GET /api/metrics` — Prometheus scrape endpoint
- Full OpenAPI/Swagger spec generated automatically.

### 4.10 CLI Tool (`atlas`)
- `atlas job enqueue --type EmailJob --payload '{"to":"x"}' --queue emails`
- `atlas job list --status failed`
- `atlas job retry <id>`
- `atlas schedule create --cron "0 * * * *" --type ReportJob`
- `atlas worker list`
- `atlas login` (stores API key locally in a config file)
- Output as human-readable table by default, `--json` flag for scripting.

---

## 5. NON-FUNCTIONAL REQUIREMENTS

- **Horizontal scalability**: any number of worker nodes can join/leave without config changes (auto-registration).
- **Resilience**: API/DB/Redis outages should degrade gracefully, not corrupt job state. Document failure-mode behavior explicitly.
- **Observability**: structured logging (Serilog, JSON output), correlation IDs propagated from API → queue → worker → logs.
- **Security**: no secrets in code, use `appsettings.{env}.json` + environment variable overrides + `dotnet user-secrets` for local dev. Rate-limit public API endpoints.
- **Testability**: core queue/scheduler logic covered by integration tests using Testcontainers (real Postgres + Redis).

---

## 6. SUGGESTED PROJECT STRUCTURE

```
Atlas/
├── src/
│   ├── Atlas.Api/                # ASP.NET Core REST API + SignalR hubs + Auth
│   ├── Atlas.Worker/             # Worker node host (can run N replicas)
│   ├── Atlas.Scheduler/          # Cron scheduler service (leader-elected)
│   ├── Atlas.Core/                # Domain models, IJobHandler, shared abstractions
│   ├── Atlas.Infrastructure/      # EF Core DbContext, Redis client, repositories
│   ├── Atlas.Cli/                 # System.CommandLine based CLI tool
│   └── Atlas.Dashboard/           # React + TS frontend (Vite)
├── tests/
│   ├── Atlas.UnitTests/
│   └── Atlas.IntegrationTests/    # Testcontainers-based
├── docker-compose.yml             # postgres, redis, api, worker(x N), dashboard
├── DECISIONS.md                   # architectural decisions log
└── README.md
```

---

## 7. BUILD MILESTONES (build in this order, don't jump ahead)

1. **Phase 1 — Core Engine**: Domain models, Postgres schema/migrations, job enqueue/claim/complete logic, single worker executing a hardcoded job type. No UI yet.
2. **Phase 2 — Reliability**: Retries with backoff, dead-letter handling, worker crash recovery (heartbeat + reaper), idempotency keys.
3. **Phase 3 — Scheduler**: Cron-based scheduled jobs with leader election.
4. **Phase 4 — API + Auth**: Full REST API, JWT + API key auth, Swagger docs.
5. **Phase 5 — Real-time**: SignalR hub for live logs + job status + worker heartbeat broadcast.
6. **Phase 6 — Dashboard**: React app — job list/detail, queue overview, worker health, live log tail, metrics charts, schedule management UI.
7. **Phase 7 — CLI**: Wrap the REST API in a polished CLI tool.
8. **Phase 8 — Metrics & Polish**: Prometheus endpoint, docker-compose full stack, README with architecture diagram, seed/demo job types (e.g. `SendEmailJob`, `GenerateReportJob`) to showcase the system end to end.

At the end of each phase: run tests, summarize what was built, list any deviations from this spec in `DECISIONS.md`.

---

## 8. DELIVERABLE EXPECTATIONS

- Fully runnable via `docker-compose up` (Postgres + Redis + API + 2 worker replicas + Dashboard).
- Seed script/demo job types so a reviewer can enqueue a job and watch it flow through the whole system live.
- README with: architecture diagram, setup instructions, API reference link, CLI usage examples.
- Code should look like it belongs in a real company's repo — not a hackathon prototype.

---

**Start with Phase 1. Confirm the domain model and Postgres schema design before writing worker/queue logic. Then proceed phase by phase, showing your work at each step.**
