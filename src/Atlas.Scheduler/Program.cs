using System;
using Atlas.Core.Interfaces;
using Atlas.Infrastructure.Data;
using Atlas.Infrastructure.Queue;
using Atlas.Infrastructure.Repositories;
using Atlas.Scheduler.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Atlas.Scheduler;

public class Program
{
    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((ctx, services) =>
            {
                var dbConn = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
                             ?? ctx.Configuration.GetConnectionString("DefaultConnection")
                             ?? "Host=localhost;Database=atlas;Username=postgres;Password=postgres";

                var redisConn = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
                                ?? ctx.Configuration.GetConnectionString("RedisConnection")
                                ?? "localhost:6379";

                services.AddDbContext<AtlasDbContext>(o => o.UseNpgsql(dbConn));

                services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
                services.AddSingleton<IRedisJobQueue, RedisJobQueue>();

                services.AddScoped<IJobRepository, JobRepository>();
                services.AddScoped<IWorkerRepository, WorkerRepository>();
                services.AddScoped<IJobLogRepository, JobLogRepository>();
                services.AddScoped<IScheduledJobRepository, ScheduledJobRepository>();
                services.AddScoped<IUnitOfWork, UnitOfWork>();

                services.AddHostedService<CronSchedulerService>();
            })
            .Build();

        await host.RunAsync();
    }
}
