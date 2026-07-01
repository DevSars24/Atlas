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
