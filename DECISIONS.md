# Atlas Design Decisions

This document outlines the key architectural and design decisions made during the implementation of the core job queue engine (Phase 1) and the reliability layer (Phase 2).

---

## 1. Dual-Engine Architecture: Postgres & Redis

**Decision:** PostgreSQL is used as the transactional source of truth, and Redis is used as the low-latency, real-time dispatch and notification layer.

- **Postgres Role:** Stores job states, logs, and status histories. Handles concurrency locking via raw SQL Common Table Expressions (CTEs) executing `SELECT ... FOR UPDATE SKIP LOCKED`.
- **Redis Role:** Acts as an ephemeral, low-latency message broker. Stores job IDs in a double-list queue layout.
- **BRPOPLPUSH Pattern:** Worker nodes query Redis using a blocking `BRPOPLPUSH` (blocking pop from main list, push to worker-specific processing list). This prevents job loss if a worker crashes immediately after popping.
- **Transactional Consistency:** Postgres remains the ultimate authority. When a worker pops a Job ID from Redis, it must successfully claim the job inside a Postgres transaction using the `ClaimNextJobAsync` atomic operation. If the job status in Postgres is not `Pending` (e.g. cancelled, or already claimed by another worker), it is immediately discarded.

---

## 2. PostgreSQL Atomic Claiming (SKIP LOCKED)

**Decision:** We use a single CTE raw SQL query in EF Core to claim and lock jobs.

```sql
WITH next_job AS (
    SELECT "Id"
    FROM "Jobs"
    WHERE "Status" = 0 -- Pending
      AND "Queue" = @Queue
      AND "ScheduledAt" <= @Now
      AND ("LockedUntil" IS NULL OR "LockedUntil" < @Now)
    ORDER BY "Priority" DESC, "ScheduledAt" ASC, "CreatedAt" ASC
    LIMIT 1
    FOR UPDATE SKIP LOCKED
)
UPDATE "Jobs"
SET "Status" = 1, -- Processing
    "LockedUntil" = @LockedUntil,
    "LockedBy" = @WorkerId,
    "Attempts" = "Attempts" + 1,
    "UpdatedAt" = @Now
FROM next_job
WHERE "Jobs"."Id" = next_job."Id"
RETURNING "Jobs".*;
```

- This ensures that under high concurrent workloads, multiple workers will not pick up the same job, and workers will not block waiting for other locks (they will skip locked records).
- The entire check, claim, lock, and version increment runs in a single database round-trip, maximizing transaction throughput.

---

## 3. Idempotency Key Handling

**Decision:** A unique database index is defined on `IdempotencyKey` in the `Jobs` table.

- In PostgreSQL, a unique constraint allows multiple `NULL` values. This is configured in EF Core using a partial unique index:
  ```csharp
  entity.HasIndex(e => e.IdempotencyKey)
        .IsUnique()
        .HasFilter("\"IdempotencyKey\" IS NOT NULL");
  ```
- In the Web API, the `POST /api/jobs` endpoint checks if a job with the same idempotency key already exists. If yes, it returns the existing job directly instead of creating a duplicate.
- A try-catch block handles potential race conditions on duplicate inserts, falling back to fetch and return the existing record.

---

## 4. Landing Page / Demo Placement

**Decision:** The static HTML/CSS/JS landing page dashboard is placed in the `wwwroot` folder of the `Atlas.Api` project.

**Reasoning:**
- **Zero CORS Issues:** Serving both the API endpoints and the dashboard from the same host/port completely bypasses CORS (Cross-Origin Resource Sharing) configuration issues in local and containerized environments.
- **Unified Deployment:** Single Docker container deployment. The `atlas-api` image hosts the dashboard files alongside the API backend.
- **Lightweight Structure:** Bypasses the need to manage Node.js, npm, or dev servers (like Vite/Next.js) for a simple dashboard, maximizing local compatibility and startup speed.

---

## 5. Enum-based LogLevel Ambiguity

**Decision:** Both `Atlas.Core.Domain` and `Microsoft.Extensions.Logging` namespaces declare a `LogLevel` enum.

- To resolve compiler ambiguity, references in background services are fully qualified to `Atlas.Core.Domain.LogLevel` when logging job execution events to the database.
- Standard dependency injection logging uses `Microsoft.Extensions.Logging.ILogger<T>` for diagnostic console traces.

---

## 6. Startup DB Migrations

**Decision:** The Web API executes `context.Database.Migrate()` on application start.

- In a containerized environment (like Docker Compose), this ensures that database schemas and indexes are automatically set up once the PostgreSQL container becomes healthy.
- Eliminates manual DB migration steps for deployment or testing.

---

## 7. Cron Scheduler — Postgres Advisory Lock Leader Election (Phase 3)

**Decision:** A single scheduler service uses `pg_try_advisory_lock(lockId)` to elect a leader across N replicas.

- **Why not Zookeeper/etcd?** Atlas already depends on Postgres — using advisory locks adds zero new infrastructure.
- **Session-level lock:** The lock is held for the duration of the DB connection. If the leader crashes, the lock is released automatically by Postgres on disconnect.
- **Tick interval:** 30 seconds. Sub-minute cron expressions are possible but not recommended; 30s provides adequate resolution for `*/1 * * * *` schedules.
- **Cronos library** evaluates standard 5-field cron expressions and computes `NextRunAt` for each schedule.

---

## 8. Authentication: Dual-Scheme (JWT + API Key) (Phase 4)

**Decision:** Two authentication schemes are registered and combined with policy-based authorization.

- **JWT (Bearer):** Used by the React dashboard. Users log in via `POST /api/auth/login`, receive a time-limited JWT signed with HMAC-SHA256.
- **API Key (X-Api-Key header):** Used by CLI tools and external integrations. Keys are stored as bcrypt hashes — the raw key is shown once at creation and cannot be retrieved again.
- **Policy-based roles:** `OperatorPlus` (Operator + Admin) protects mutating endpoints. `AdminOnly` protects key management. Read endpoints are open.
- **SignalR token:** The JWT is passed via `?access_token=` query string for SignalR WebSocket upgrades (browsers can't set headers on WS connections).

---

## 9. SignalR Real-time Logs (Phase 5)

**Decision:** `JobLogHub` uses group-scoped broadcasting (`job:{jobId}`) so clients only receive logs for jobs they're watching.

- **Backplane:** For single-instance deployments, in-process SignalR is sufficient. For multi-instance, a Redis backplane can be added via `AddStackExchangeRedis()` — planned as a Phase 9 enhancement.
- **Hybrid log view:** The `JobDetail` page shows historical logs from Postgres plus live streaming logs from SignalR in a unified view.

---

## 10. React Dashboard Architecture (Phase 6)

**Decision:** Vite + React + TypeScript, served separately in development, built into `wwwroot` for production.

- **Build output:** `vite build` writes to `src/Atlas.Api/wwwroot/` — the API serves it as static files. This means a single Docker image contains both API and Dashboard.
- **API proxy:** In dev mode, Vite proxies `/api` and `/hubs` to `http://localhost:5000` so no CORS issues.
- **Recharts** for metrics charts (lightweight, no extra dependencies).
- **@microsoft/signalr** for live log streaming in `JobDetail`.

---

## 11. CLI Design (Phase 7)

**Decision:** `System.CommandLine` for argument parsing + `Spectre.Console` for rich terminal output (tables, colors).

- Config stored at `~/.atlas/config.json` — supports both API key and JWT token auth modes.
- Output defaults to human-readable tables; `--json` flag enables machine-readable output for scripting.
- Binary name is `atlas` (configured via `<AssemblyName>atlas</AssemblyName>` in csproj).

---

## 12. Prometheus Metrics (Phase 8)

**Decision:** `prometheus-net.AspNetCore` exposes `/metrics` for Prometheus scraping.

- Counters: `atlas_jobs_enqueued_total`, `atlas_jobs_succeeded_total`, `atlas_jobs_failed_total`
- Gauges: `atlas_jobs_active`, `atlas_queue_depth`
- Histogram: `atlas_job_duration_seconds` (per job type)
- Grafana dashboards can be wired to these metrics for production observability.
