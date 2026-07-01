using Atlas.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Data;

public class AtlasDbContext : DbContext
{
    public AtlasDbContext(DbContextOptions<AtlasDbContext> options) : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobStatusHistory> JobStatusHistories => Set<JobStatusHistory>();
    public DbSet<JobLog> JobLogs => Set<JobLog>();
    public DbSet<WorkerNode> Workers => Set<WorkerNode>();
    public DbSet<ScheduledJob> ScheduledJobs => Set<ScheduledJob>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Job Configurations
        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Queue).IsRequired().HasMaxLength(250);
            entity.Property(e => e.JobType).IsRequired().HasMaxLength(250);
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.LockedBy).HasMaxLength(250);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(250);
            entity.Property(e => e.LastError);

            // Unique constraint on IdempotencyKey
            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique()
                .HasFilter("\"IdempotencyKey\" IS NOT NULL"); // PostgreSQL syntax for partial unique index to allow multiple nulls

            // Index for ClaimNextJobAsync
            entity.HasIndex(e => new { e.Queue, e.Status, e.Priority, e.ScheduledAt });
        });

        // JobStatusHistory Configurations
        modelBuilder.Entity<JobStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Notes).HasMaxLength(1000);

            entity.HasOne(e => e.Job)
                .WithMany(j => j.StatusHistory)
                .HasForeignKey(e => e.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // JobLog Configurations
        modelBuilder.Entity<JobLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Message).IsRequired();

            entity.HasOne(e => e.Job)
                .WithMany(j => j.Logs)
                .HasForeignKey(e => e.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // WorkerNode Configurations
        modelBuilder.Entity<WorkerNode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(250);
        });

        // ScheduledJob Configurations
        modelBuilder.Entity<ScheduledJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CronExpression).IsRequired().HasMaxLength(250);
            entity.Property(e => e.JobType).IsRequired().HasMaxLength(250);
            entity.Property(e => e.Queue).IsRequired().HasMaxLength(250);
            entity.Property(e => e.Payload).IsRequired();
        });

        // Workflow Configurations
        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(250);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.DefinitionJson).IsRequired();
        });

        modelBuilder.Entity<WorkflowRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);

            entity.HasOne(e => e.WorkflowDefinition)
                .WithMany()
                .HasForeignKey(e => e.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
