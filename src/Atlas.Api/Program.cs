using System;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using Atlas.Api.Auth;
using Atlas.Api.Hubs;
using Atlas.Api.Services;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Atlas.Infrastructure.Queue;
using Atlas.Infrastructure.Repositories;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi;

namespace Atlas.Api;

// ── Request records ──────────────────────────────────────────────────────────

public record CreateJobRequest(
    string Queue,
    string JobType,
    string Payload,
    JobPriority Priority = JobPriority.Normal,
    string? IdempotencyKey = null,
    int MaxAttempts = 3,
    DateTimeOffset? ScheduledAt = null
);

public record CreateScheduleRequest(
    string Name,
    string CronExpression,
    string JobType,
    string Queue,
    string Payload = "{}",
    JobPriority Priority = JobPriority.Normal,
    int MaxAttempts = 3,
    bool IsEnabled = true,
    MisfirePolicy MisfirePolicy = MisfirePolicy.Skip,
    string? Description = null
);

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password, string Username, string Role = "Viewer");
public record CreateApiKeyRequest(string Name, string Role = "Viewer");

// ── Program ───────────────────────────────────────────────────────────────────

public class Program
{
    private const string ApiKeyScheme = "ApiKey";
    private const string PolicyOperatorPlus = "OperatorPlus";
    private const string PolicyAdminOnly = "AdminOnly";

    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ── Config ────────────────────────────────────────────────────────────
        var dbConn = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
                     ?? builder.Configuration.GetConnectionString("DefaultConnection")
                     ?? "Host=localhost;Database=atlas;Username=postgres;Password=postgres";

        var redisConn = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
                        ?? builder.Configuration.GetConnectionString("RedisConnection")
                        ?? "localhost:6379";

        var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
                        ?? builder.Configuration["Jwt:Secret"]
                        ?? "change-me-in-production-must-be-at-least-32-chars";

        var jwtIssuer   = Environment.GetEnvironmentVariable("JWT_ISSUER")   ?? builder.Configuration["Jwt:Issuer"]   ?? "atlas-api";
        var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? builder.Configuration["Jwt:Audience"] ?? "atlas-dashboard";
        var jwtExpiry   = int.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRY_MINUTES"), out var exp) ? exp : 60;

        var adminApiKey = Environment.GetEnvironmentVariable("API_ADMIN_KEY");

        // ── Database & Identity ───────────────────────────────────────────────
        builder.Services.AddDbContext<AtlasDbContext>(o => o.UseNpgsql(dbConn));

        builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
        {
            o.Password.RequireDigit = false;
            o.Password.RequiredLength = 6;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireUppercase = false;
        })
        .AddEntityFrameworkStores<AtlasDbContext>()
        .AddDefaultTokenProviders();

        // ── Redis ─────────────────────────────────────────────────────────────
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
        builder.Services.AddSingleton<IRedisJobQueue, RedisJobQueue>();

        // ── Repositories ──────────────────────────────────────────────────────
        builder.Services.AddScoped<IJobRepository, JobRepository>();
        builder.Services.AddScoped<IWorkerRepository, WorkerRepository>();
        builder.Services.AddScoped<IJobLogRepository, JobLogRepository>();
        builder.Services.AddScoped<IScheduledJobRepository, ScheduledJobRepository>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ── JWT Auth ──────────────────────────────────────────────────────────
        var jwtSvc = new JwtService(jwtSecret, jwtIssuer, jwtAudience, jwtExpiry);
        builder.Services.AddSingleton(jwtSvc);

        builder.Services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(o =>
        {
            o.TokenValidationParameters = jwtSvc.GetValidationParameters();
            // Allow token in query string for SignalR
            o.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var accessToken = ctx.Request.Query["access_token"];
                    var path = ctx.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        ctx.Token = accessToken;
                    return System.Threading.Tasks.Task.CompletedTask;
                }
            };
        })
        .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(ApiKeyScheme, _ => { });

        builder.Services.AddAuthorization(o =>
        {
            // JWT or API key satisfies these policies
            o.AddPolicy(PolicyOperatorPlus, p =>
            {
                p.RequireAuthenticatedUser();
                p.RequireRole("Operator", "Admin");
                p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyScheme);
            });
            o.AddPolicy(PolicyAdminOnly, p =>
            {
                p.RequireAuthenticatedUser();
                p.RequireRole("Admin");
                p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyScheme);
            });
        });

        // ── SignalR ───────────────────────────────────────────────────────────
        builder.Services.AddSignalR();
        builder.Services.AddScoped<JobHubNotifier>();
        builder.Services.AddHostedService<RedisLogSubscriberService>();

        // ── Serialization ─────────────────────────────────────────────────────
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // ── Swagger ───────────────────────────────────────────────────────────
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() { Title = "Atlas API", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization", Type = SecuritySchemeType.Http,
                Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header
            });
            c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Name = "X-Api-Key", Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header
            });
        });

        // ── CORS ──────────────────────────────────────────────────────────────
        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        // ── Prometheus Metrics ────────────────────────────────────────────────
        // Counter/gauge objects shared via DI
        builder.Services.AddSingleton(Metrics.CreateCounter("atlas_jobs_enqueued_total", "Total jobs enqueued", new CounterConfiguration { LabelNames = ["queue", "job_type"] }));
        builder.Services.AddSingleton(Metrics.CreateGauge("atlas_jobs_active", "Currently active (Processing) jobs"));
        builder.Services.AddSingleton(Metrics.CreateGauge("atlas_queue_depth", "Jobs in Pending status", new GaugeConfiguration { LabelNames = ["queue"] }));
        builder.Services.AddSingleton(Metrics.CreateCounter("atlas_jobs_succeeded_total", "Total succeeded jobs"));
        builder.Services.AddSingleton(Metrics.CreateCounter("atlas_jobs_failed_total", "Total failed/dead-lettered jobs"));
        builder.Services.AddSingleton(Metrics.CreateHistogram("atlas_job_duration_seconds", "Job execution duration", new HistogramConfiguration { LabelNames = ["job_type"] }));

        var app = builder.Build();

        // ── Middleware ────────────────────────────────────────────────────────
        app.UseCors();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Atlas API V1");
            c.RoutePrefix = "swagger";
        });

        // Prometheus scrape endpoint
        app.MapMetrics("/metrics");

        // SignalR hub
        app.MapHub<JobLogHub>("/hubs/joblogs");

        // ── Startup tasks ─────────────────────────────────────────────────────
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
            try
            {
                db.Database.Migrate();
            }
            catch (Exception ex)
            {
                var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                log.LogWarning("Migration failed: {Msg}", ex.Message);
            }

            // Seed admin API key from env (only if table is empty)
            if (!string.IsNullOrWhiteSpace(adminApiKey) && !db.ApiKeys.Any())
            {
                db.ApiKeys.Add(new ApiKey
                {
                    Id = Guid.NewGuid(),
                    Name = "admin-bootstrap",
                    KeyHash = BCrypt.Net.BCrypt.HashPassword(adminApiKey),
                    Role = "Admin",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                db.SaveChanges();
            }

            // Seed roles for Identity
            var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in new[] { "Admin", "Operator", "Viewer" })
            {
                if (!await roleMgr.RoleExistsAsync(role))
                    await roleMgr.CreateAsync(new IdentityRole(role));
            }
        }

        // ── Endpoints ─────────────────────────────────────────────────────────

        // Health
        app.MapGet("/health", () => Results.Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTimeOffset.UtcNow,
            Version = "2.0.0"
        })).WithName("GetHealth").WithOpenApi();

        // ── Auth endpoints ────────────────────────────────────────────────────

        app.MapPost("/api/auth/register", async (
            RegisterRequest req,
            UserManager<ApplicationUser> userMgr,
            RoleManager<IdentityRole> roleMgr) =>
        {
            var user = new ApplicationUser
            {
                UserName = req.Username,
                Email = req.Email,
                Role = req.Role
            };
            var result = await userMgr.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return Results.BadRequest(new { Errors = result.Errors.Select(e => e.Description) });

            if (await roleMgr.RoleExistsAsync(req.Role))
                await userMgr.AddToRoleAsync(user, req.Role);

            return Results.Ok(new { user.Id, user.Email, user.UserName, user.Role });
        }).WithName("RegisterUser").WithOpenApi();

        app.MapPost("/api/auth/login", async (
            LoginRequest req,
            UserManager<ApplicationUser> userMgr,
            JwtService jwtSvc2) =>
        {
            var user = await userMgr.FindByEmailAsync(req.Email);
            if (user == null || !await userMgr.CheckPasswordAsync(user, req.Password))
                return Results.Unauthorized();

            var token = jwtSvc2.GenerateToken(user);
            return Results.Ok(new { Token = token, ExpiresIn = jwtExpiry * 60 });
        }).WithName("LoginUser").WithOpenApi();

        // ── API Key management (Admin only) ───────────────────────────────────

        app.MapGet("/api/apikeys", async (AtlasDbContext db) =>
        {
            var keys = await db.ApiKeys
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => new { k.Id, k.Name, k.Role, k.IsActive, k.CreatedAt, k.LastUsedAt })
                .ToListAsync();
            return Results.Ok(keys);
        }).RequireAuthorization(PolicyAdminOnly).WithName("GetApiKeys").WithOpenApi();

        app.MapPost("/api/apikeys", async (CreateApiKeyRequest req, AtlasDbContext db) =>
        {
            var rawKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var apiKey = new ApiKey
            {
                Id = Guid.NewGuid(),
                Name = req.Name,
                KeyHash = BCrypt.Net.BCrypt.HashPassword(rawKey),
                Role = req.Role,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ApiKeys.Add(apiKey);
            await db.SaveChangesAsync();
            // Return raw key ONCE — it cannot be retrieved again
            return Results.Ok(new { apiKey.Id, apiKey.Name, apiKey.Role, RawKey = rawKey });
        }).RequireAuthorization(PolicyAdminOnly).WithName("CreateApiKey").WithOpenApi();

        app.MapDelete("/api/apikeys/{id:guid}", async (Guid id, AtlasDbContext db) =>
        {
            var key = await db.ApiKeys.FindAsync(id);
            if (key == null) return Results.NotFound();
            key.IsActive = false;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization(PolicyAdminOnly).WithName("RevokeApiKey").WithOpenApi();

        // ── Job endpoints ─────────────────────────────────────────────────────

        app.MapPost("/api/jobs", async (
            CreateJobRequest request,
            IUnitOfWork uow,
            IRedisJobQueue redisQueue) =>
        {
            if (string.IsNullOrWhiteSpace(request.Queue) || string.IsNullOrWhiteSpace(request.JobType))
                return Results.BadRequest(new { Error = "Queue and JobType are required." });

            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existing = await uow.Jobs.GetByIdempotencyKeyAsync(request.IdempotencyKey);
                if (existing != null) return Results.Ok(existing);
            }

            var now = DateTimeOffset.UtcNow;
            var job = new Job
            {
                Id = Guid.NewGuid(),
                Queue = request.Queue,
                JobType = request.JobType,
                Payload = request.Payload ?? "{}",
                Priority = request.Priority,
                Status = JobStatus.Pending,
                Attempts = 0,
                MaxAttempts = request.MaxAttempts <= 0 ? 3 : request.MaxAttempts,
                ScheduledAt = request.ScheduledAt ?? now,
                CreatedAt = now,
                UpdatedAt = now,
                IdempotencyKey = request.IdempotencyKey
            };

            job.StatusHistory.Add(new JobStatusHistory
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                FromStatus = JobStatus.Pending,
                ToStatus = JobStatus.Pending,
                Timestamp = now,
                Notes = "Job enqueued."
            });

            try
            {
                await uow.Jobs.AddAsync(job);
                await uow.SaveChangesAsync();
            }
            catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existing = await uow.Jobs.GetByIdempotencyKeyAsync(request.IdempotencyKey);
                if (existing != null) return Results.Ok(existing);
                throw;
            }

            if (job.ScheduledAt <= now)
                await redisQueue.PushJobIdAsync(job.Queue, job.Id);

            return Results.Created($"/api/jobs/{job.Id}", job);
        }).RequireAuthorization(PolicyOperatorPlus).WithName("EnqueueJob").WithOpenApi();

        app.MapGet("/api/jobs/{id:guid}", async (Guid id, IUnitOfWork uow) =>
        {
            var job = await uow.Jobs.GetByIdWithRelationsAsync(id);
            return job != null ? Results.Ok(job) : Results.NotFound();
        }).WithName("GetJobById").WithOpenApi();

        app.MapGet("/api/jobs", async (string? queue, JobStatus? status, int? page, int? pageSize, IUnitOfWork uow) =>
        {
            var jobs = await uow.Jobs.GetJobsAsync(queue, status, page ?? 1, pageSize ?? 20);
            return Results.Ok(jobs);
        }).WithName("GetJobs").WithOpenApi();

        app.MapPost("/api/jobs/{id:guid}/retry", async (Guid id, IUnitOfWork uow, IRedisJobQueue redisQueue) =>
        {
            var job = await uow.Jobs.GetByIdAsync(id);
            if (job == null) return Results.NotFound();

            if (job.Status != JobStatus.Failed && job.Status != JobStatus.DeadLettered)
                return Results.BadRequest(new { Error = $"Only failed/dead-lettered jobs can be retried. Current: {job.Status}" });

            var old = job.Status;
            var now = DateTimeOffset.UtcNow;
            job.Status = JobStatus.Pending;
            job.Attempts = 0;
            job.ScheduledAt = now;
            job.UpdatedAt = now;
            job.LastError = null;

            job.StatusHistory.Add(new JobStatusHistory
            {
                Id = Guid.NewGuid(), JobId = job.Id,
                FromStatus = old, ToStatus = JobStatus.Pending,
                Timestamp = now, Notes = "Manual retry triggered."
            });

            await uow.Logs.AddLogAsync(job.Id, Atlas.Core.Domain.LogLevel.Info, "Manual retry triggered from API.");
            await uow.SaveChangesAsync();
            await redisQueue.PushJobIdAsync(job.Queue, job.Id);

            return Results.Ok(job);
        }).RequireAuthorization(PolicyOperatorPlus).WithName("RetryJob").WithOpenApi();

        app.MapDelete("/api/jobs/{id:guid}", async (Guid id, IUnitOfWork uow, IRedisJobQueue redisQueue) =>
        {
            var job = await uow.Jobs.GetByIdAsync(id);
            if (job == null) return Results.NotFound();
            await uow.Jobs.DeleteAsync(job);
            await uow.SaveChangesAsync();
            await redisQueue.RemoveFromQueueAsync(job.Queue, job.Id);
            return Results.NoContent();
        }).RequireAuthorization(PolicyOperatorPlus).WithName("CancelJob").WithOpenApi();

        // ── Workers ───────────────────────────────────────────────────────────

        app.MapGet("/api/workers", async (IUnitOfWork uow) =>
        {
            var workers = await uow.Workers.GetAllAsync();
            return Results.Ok(workers);
        }).WithName("GetWorkers").WithOpenApi();

        // ── Schedules ─────────────────────────────────────────────────────────

        app.MapGet("/api/schedules", async (IUnitOfWork uow) =>
        {
            var schedules = await uow.ScheduledJobs.GetAllAsync();
            return Results.Ok(schedules);
        }).WithName("GetSchedules").WithOpenApi();

        app.MapGet("/api/schedules/{id:guid}", async (Guid id, IUnitOfWork uow) =>
        {
            var s = await uow.ScheduledJobs.GetByIdAsync(id);
            return s != null ? Results.Ok(s) : Results.NotFound();
        }).WithName("GetScheduleById").WithOpenApi();

        app.MapPost("/api/schedules", async (CreateScheduleRequest req, IUnitOfWork uow) =>
        {
            // Validate cron expression
            try { Cronos.CronExpression.Parse(req.CronExpression, Cronos.CronFormat.Standard); }
            catch { return Results.BadRequest(new { Error = "Invalid cron expression." }); }

            var now = DateTimeOffset.UtcNow;

            // Compute first NextRunAt
            DateTimeOffset? nextRun = null;
            try
            {
                var expr = Cronos.CronExpression.Parse(req.CronExpression, Cronos.CronFormat.Standard);
                var next = expr.GetNextOccurrence(now.UtcDateTime, TimeZoneInfo.Utc);
                if (next.HasValue) nextRun = new DateTimeOffset(next.Value, TimeSpan.Zero);
            }
            catch { }

            var schedule = new ScheduledJob
            {
                Id = Guid.NewGuid(),
                Name = req.Name,
                Description = req.Description,
                CronExpression = req.CronExpression,
                JobType = req.JobType,
                Queue = req.Queue,
                Payload = req.Payload,
                Priority = req.Priority,
                MaxAttempts = req.MaxAttempts,
                IsEnabled = req.IsEnabled,
                MisfirePolicy = req.MisfirePolicy,
                NextRunAt = nextRun,
                CreatedAt = now,
                UpdatedAt = now
            };

            await uow.ScheduledJobs.AddAsync(schedule);
            await uow.SaveChangesAsync();

            return Results.Created($"/api/schedules/{schedule.Id}", schedule);
        }).RequireAuthorization(PolicyOperatorPlus).WithName("CreateSchedule").WithOpenApi();

        app.MapPut("/api/schedules/{id:guid}", async (Guid id, CreateScheduleRequest req, IUnitOfWork uow) =>
        {
            var schedule = await uow.ScheduledJobs.GetByIdAsync(id);
            if (schedule == null) return Results.NotFound();

            try { Cronos.CronExpression.Parse(req.CronExpression, Cronos.CronFormat.Standard); }
            catch { return Results.BadRequest(new { Error = "Invalid cron expression." }); }

            schedule.Name = req.Name;
            schedule.Description = req.Description;
            schedule.CronExpression = req.CronExpression;
            schedule.JobType = req.JobType;
            schedule.Queue = req.Queue;
            schedule.Payload = req.Payload;
            schedule.Priority = req.Priority;
            schedule.MaxAttempts = req.MaxAttempts;
            schedule.IsEnabled = req.IsEnabled;
            schedule.MisfirePolicy = req.MisfirePolicy;
            schedule.UpdatedAt = DateTimeOffset.UtcNow;

            // Recompute NextRunAt
            try
            {
                var expr = Cronos.CronExpression.Parse(req.CronExpression, Cronos.CronFormat.Standard);
                var next = expr.GetNextOccurrence(DateTimeOffset.UtcNow.UtcDateTime, TimeZoneInfo.Utc);
                schedule.NextRunAt = next.HasValue ? new DateTimeOffset(next.Value, TimeSpan.Zero) : null;
            }
            catch { }

            await uow.ScheduledJobs.UpdateAsync(schedule);
            await uow.SaveChangesAsync();
            return Results.Ok(schedule);
        }).RequireAuthorization(PolicyOperatorPlus).WithName("UpdateSchedule").WithOpenApi();

        app.MapDelete("/api/schedules/{id:guid}", async (Guid id, IUnitOfWork uow) =>
        {
            var s = await uow.ScheduledJobs.GetByIdAsync(id);
            if (s == null) return Results.NotFound();
            await uow.ScheduledJobs.DeleteAsync(s);
            await uow.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization(PolicyOperatorPlus).WithName("DeleteSchedule").WithOpenApi();

        app.MapPost("/api/schedules/{id:guid}/trigger", async (
            Guid id, IUnitOfWork uow, IRedisJobQueue redisQueue) =>
        {
            var schedule = await uow.ScheduledJobs.GetByIdAsync(id);
            if (schedule == null) return Results.NotFound();

            var now = DateTimeOffset.UtcNow;
            var job = new Job
            {
                Id = Guid.NewGuid(),
                Queue = schedule.Queue,
                JobType = schedule.JobType,
                Payload = schedule.Payload,
                Priority = schedule.Priority,
                Status = JobStatus.Pending,
                Attempts = 0,
                MaxAttempts = schedule.MaxAttempts,
                ScheduledAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            job.StatusHistory.Add(new JobStatusHistory
            {
                Id = Guid.NewGuid(), JobId = job.Id,
                FromStatus = JobStatus.Pending, ToStatus = JobStatus.Pending,
                Timestamp = now, Notes = $"Manually triggered from schedule '{schedule.Name}'"
            });
            await uow.Jobs.AddAsync(job);
            await uow.SaveChangesAsync();
            await redisQueue.PushJobIdAsync(job.Queue, job.Id);
            return Results.Ok(job);
        }).RequireAuthorization(PolicyOperatorPlus).WithName("TriggerSchedule").WithOpenApi();

        // ── Stats for dashboard ───────────────────────────────────────────────

        app.MapGet("/api/stats", async (IUnitOfWork uow, AtlasDbContext db) =>
        {
            var total     = await uow.Jobs.GetCountAsync();
            var pending   = await uow.Jobs.GetCountAsync(JobStatus.Pending);
            var processing = await uow.Jobs.GetCountAsync(JobStatus.Processing);
            var succeeded = await uow.Jobs.GetCountAsync(JobStatus.Succeeded);
            var failed    = await uow.Jobs.GetCountAsync(JobStatus.Failed);
            var dlq       = await uow.Jobs.GetCountAsync(JobStatus.DeadLettered);
            var workers   = (await uow.Workers.GetAllAsync()).Count;
            var schedules = (await uow.ScheduledJobs.GetAllAsync()).Count;

            return Results.Ok(new
            {
                Total = total,
                Pending = pending,
                Processing = processing,
                Succeeded = succeeded,
                Failed = failed,
                DeadLettered = dlq,
                Workers = workers,
                Schedules = schedules
            });
        }).WithName("GetStats").WithOpenApi();

        // ── Seed demo data ────────────────────────────────────────────────────

        app.MapPost("/api/seed", async (IUnitOfWork uow, IRedisJobQueue redisQueue) =>
        {
            var now = DateTimeOffset.UtcNow;
            var demoJobs = new[]
            {
                ("SendEmailJob",    "emails",  """{"to":"alice@example.com","subject":"Welcome!","body":"Hello Alice"}"""),
                ("SendEmailJob",    "emails",  """{"to":"bob@example.com","subject":"Report Ready","body":"Your report is ready"}"""),
                ("GenerateReportJob", "reports", """{"reportType":"Monthly","format":"PDF","recipients":["cfo@example.com"]}"""),
                ("GenerateReportJob", "reports", """{"reportType":"Weekly","format":"CSV","recipients":["team@example.com"]}"""),
            };

            var created = new System.Collections.Generic.List<Guid>();
            foreach (var (type, queue, payload) in demoJobs)
            {
                var job = new Job
                {
                    Id = Guid.NewGuid(), Queue = queue, JobType = type, Payload = payload,
                    Priority = JobPriority.Normal, Status = JobStatus.Pending,
                    Attempts = 0, MaxAttempts = 3,
                    ScheduledAt = now, CreatedAt = now, UpdatedAt = now
                };
                job.StatusHistory.Add(new JobStatusHistory
                {
                    Id = Guid.NewGuid(), JobId = job.Id,
                    FromStatus = JobStatus.Pending, ToStatus = JobStatus.Pending,
                    Timestamp = now, Notes = "Demo seed job."
                });
                await uow.Jobs.AddAsync(job);
                created.Add(job.Id);
            }

            // Demo schedule
            if (!(await uow.ScheduledJobs.GetAllAsync()).Any())
            {
                var nextRun = DateTimeOffset.UtcNow.AddMinutes(1);
                await uow.ScheduledJobs.AddAsync(new ScheduledJob
                {
                    Id = Guid.NewGuid(), Name = "Daily Email Digest",
                    Description = "Sends a daily email digest every morning",
                    CronExpression = "0 8 * * *", JobType = "SendEmailJob",
                    Queue = "emails", Payload = """{"to":"team@example.com","subject":"Daily Digest","body":"Your daily summary"}""",
                    Priority = JobPriority.Normal, MaxAttempts = 3,
                    IsEnabled = true, MisfirePolicy = MisfirePolicy.Skip,
                    NextRunAt = nextRun, CreatedAt = now, UpdatedAt = now
                });
            }

            await uow.SaveChangesAsync();
            foreach (var id in created)
                await redisQueue.PushJobIdAsync("emails", id);

            return Results.Ok(new { Seeded = created.Count, Message = "Demo jobs and schedule created." });
        }).RequireAuthorization(PolicyOperatorPlus).WithName("SeedDemo").WithOpenApi();

        await app.RunAsync();
    }
}
