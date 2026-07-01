using System;
using System.Text.Json.Serialization;
using Atlas.Core.Domain;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Atlas.Infrastructure.Queue;
using Atlas.Infrastructure.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Atlas.Api;

public record CreateJobRequest(
    string Queue,
    string JobType,
    string Payload,
    JobPriority Priority = JobPriority.Normal,
    string? IdempotencyKey = null,
    int MaxAttempts = 3,
    DateTimeOffset? ScheduledAt = null
);

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Load connection strings from configurations or env variables
        var dbConnString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") 
                           ?? builder.Configuration.GetConnectionString("DefaultConnection") 
                           ?? "Host=localhost;Database=atlas;Username=postgres;Password=postgres";

        var redisConnString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") 
                              ?? builder.Configuration.GetConnectionString("RedisConnection") 
                              ?? "localhost:6379";

        // Add services to the container
        builder.Services.AddDbContext<AtlasDbContext>(options =>
            options.UseNpgsql(dbConnString));

        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => 
            ConnectionMultiplexer.Connect(redisConnString));

        builder.Services.AddSingleton<IRedisJobQueue, RedisJobQueue>();
        builder.Services.AddScoped<IJobRepository, JobRepository>();
        builder.Services.AddScoped<IWorkerRepository, WorkerRepository>();
        builder.Services.AddScoped<IJobLogRepository, JobLogRepository>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Configure JSON options to serialize enums as strings for better readability in API response
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        // Add Swagger/OpenAPI
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() { Title = "Atlas API", Version = "v1" });
        });

        // Add CORS
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        var app = builder.Build();

        // Enable Cors
        app.UseCors();

        // Enable default files (index.html) and static files from wwwroot
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Enable Swagger UI
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Atlas API V1");
            c.RoutePrefix = "swagger"; // Swagger UI at http://localhost:5000/swagger
        });

        // 1. Health check endpoint
        app.MapGet("/health", () => Results.Ok(new 
        { 
            Status = "Healthy", 
            Timestamp = DateTimeOffset.UtcNow,
            Version = "1.0.0"
        })).WithName("GetHealth").WithOpenApi();

        // 2. POST /api/jobs (with idempotency support)
        app.MapPost("/api/jobs", async (CreateJobRequest request, IUnitOfWork uow, IRedisJobQueue redisQueue) =>
        {
            if (string.IsNullOrWhiteSpace(request.Queue) || string.IsNullOrWhiteSpace(request.JobType))
            {
                return Results.BadRequest(new { Error = "Queue and JobType are required." });
            }

            // Check unique IdempotencyKey
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existingJob = await uow.Jobs.GetByIdempotencyKeyAsync(request.IdempotencyKey);
                if (existingJob != null)
                {
                    return Results.Ok(existingJob);
                }
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
                // In case of concurrency race condition
                var existingJob = await uow.Jobs.GetByIdempotencyKeyAsync(request.IdempotencyKey);
                if (existingJob != null)
                {
                    return Results.Ok(existingJob);
                }
                throw;
            }

            // Push notification to Redis list immediately if scheduled for now or in the past
            if (job.ScheduledAt <= now)
            {
                await redisQueue.PushJobIdAsync(job.Queue, job.Id);
            }

            return Results.Created($"/api/jobs/{job.Id}", job);
        }).WithName("EnqueueJob").WithOpenApi();

        // 3. GET /api/jobs/{id}
        app.MapGet("/api/jobs/{id:guid}", async (Guid id, IUnitOfWork uow) =>
        {
            var job = await uow.Jobs.GetByIdWithRelationsAsync(id);
            return job != null ? Results.Ok(job) : Results.NotFound();
        }).WithName("GetJobById").WithOpenApi();

        // 4. GET /api/jobs (with filtering and pagination)
        app.MapGet("/api/jobs", async (string? queue, JobStatus? status, int? page, int? pageSize, IUnitOfWork uow) =>
        {
            var p = page ?? 1;
            var ps = pageSize ?? 20;
            var jobs = await uow.Jobs.GetJobsAsync(queue, status, p, ps);
            return Results.Ok(jobs);
        }).WithName("GetJobs").WithOpenApi();

        // 5. POST /api/jobs/{id}/retry
        app.MapPost("/api/jobs/{id:guid}/retry", async (Guid id, IUnitOfWork uow, IRedisJobQueue redisQueue) =>
        {
            var job = await uow.Jobs.GetByIdAsync(id);
            if (job == null)
            {
                return Results.NotFound();
            }

            if (job.Status != JobStatus.Failed && job.Status != JobStatus.DeadLettered)
            {
                return Results.BadRequest(new { Error = $"Only failed or dead-lettered jobs can be retried. Current status: {job.Status}" });
            }

            var oldStatus = job.Status;
            var now = DateTimeOffset.UtcNow;
            job.Status = JobStatus.Pending;
            job.Attempts = 0;
            job.ScheduledAt = now;
            job.UpdatedAt = now;
            job.LastError = null;

            job.StatusHistory.Add(new JobStatusHistory
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                FromStatus = oldStatus,
                ToStatus = JobStatus.Pending,
                Timestamp = now,
                Notes = "Manual retry triggered."
            });

            await uow.Logs.AddLogAsync(job.Id, Atlas.Core.Domain.LogLevel.Info, "Manual retry triggered from API.", cancellationToken: default);
            await uow.SaveChangesAsync();

            // Push to Redis queue
            await redisQueue.PushJobIdAsync(job.Queue, job.Id);

            return Results.Ok(job);
        }).WithName("RetryJob").WithOpenApi();

        // 6. DELETE /api/jobs/{id}
        app.MapDelete("/api/jobs/{id:guid}", async (Guid id, IUnitOfWork uow, IRedisJobQueue redisQueue) =>
        {
            var job = await uow.Jobs.GetByIdAsync(id);
            if (job == null)
            {
                return Results.NotFound();
            }

            await uow.Jobs.DeleteAsync(job);
            await uow.SaveChangesAsync();

            // Attempt to clean from Redis queue list
            await redisQueue.RemoveFromQueueAsync(job.Queue, job.Id);

            return Results.NoContent();
        }).WithName("CancelJob").WithOpenApi();

        // 7. GET /api/workers
        app.MapGet("/api/workers", async (IUnitOfWork uow) =>
        {
            var workers = await uow.Workers.GetAllAsync();
            return Results.Ok(workers);
        }).WithName("GetWorkers").WithOpenApi();

        // Database auto-migration during startup (useful for Docker compose orchestration)
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to apply startup migrations. Ensure database is running. Error: {ex.Message}");
        }

        app.Run();
    }
}
