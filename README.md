# Atlas — Distributed Job Queue Engine

Atlas is a high-throughput, resilient distributed job queue engine built in .NET 10. It utilizes PostgreSQL as a transactional source of truth for job states, locks, and logs, alongside Redis as a low-latency, real-time message dispatch and notification layer.

## Architecture Highlights

- **Dual-Engine Model:** Redis is the dispatch and notification layer (ephemeral, low latency) while PostgreSQL acts as the source of truth (resilient, transactional).
- **Atomic Claims:** Employs raw SQL CTEs executing `SELECT ... FOR UPDATE SKIP LOCKED` in Postgres to prevent race conditions and double-delivery without blocking workers.
- **Reliability Layer:** Custom retry policy implementation supporting exponential backoff with AWS-style Full Jitter, per-job-type options, and dead-lettering (DLQ) support.
- **Worker Heartbeats:** Workers tick every 15 seconds to update their state and concurrency loads. Expired job locks and stale workers are reaped automatically.
- **Embedded Dashboard:** Built-in static landing page serving real-time statistics, job list feed, and execution logs directly from the API endpoint.

---

## Getting Started

### Prerequisites
- .NET 10 SDK
- Docker & Docker Compose

### Running Locally with Docker Compose
To spin up the entire environment (Postgres, Redis, API, and Background Worker):
```bash
docker-compose up --build
```

Once started:
- **API Swagger documentation:** [http://localhost:5000/swagger](http://localhost:5000/swagger)
- **Engine Dashboard / Landing Page:** [http://localhost:5000/](http://localhost:5000/)
- **API Health check:** `curl http://localhost:5000/health`

---

## Running Tests

### Unit Tests
Tests mathematical retry policies, jitter offsets, and scheduled job stubs:
```bash
dotnet test tests/Atlas.UnitTests
```

### Integration Tests (Docker Required)
Spin up real PostgreSQL and Redis containers using Testcontainers to test job claims, retries, dead-lettering, heartbeats, and idempotency:
```bash
dotnet test tests/Atlas.IntegrationTests
```

---

## Project Structure

- **`Atlas.Core`:** Domain models (`Job`, `WorkerNode`, `JobLog`, etc.), enums, and interfaces.
- **`Atlas.Infrastructure`:** Entity Framework Core DB Context, repository patterns, raw SQL skip-locked claim queries, and the StackExchange.Redis queue wrapper.
- **`Atlas.Worker`:** Background hosting worker services (Polling, heartbeats, reapers, and executor).
- **`Atlas.Api`:** ASP.NET Core minimal Web API endpoints, Swagger integration, and the HTML dashboard UI.
- **`tests/`:** Integration and Unit test suites.
- **`DECISIONS.md`:** Detailed documentation of core architectural and technical decisions.
