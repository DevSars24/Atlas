using System;
using System.Threading.Tasks;
using Atlas.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace Atlas.IntegrationTests;

public class IntegrationTestFixture : IAsyncLifetime
{
    public PostgreSqlContainer PostgreSqlContainer { get; } = new PostgreSqlBuilder()
        .WithDatabase("atlas_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public RedisContainer RedisContainer { get; } = new RedisBuilder()
        .Build();

    public string DbConnectionString => PostgreSqlContainer.GetConnectionString();
    public string RedisConnectionString => RedisContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        await PostgreSqlContainer.StartAsync();
        await RedisContainer.StartAsync();

        // Apply migrations automatically to spin up schema in the container
        var optionsBuilder = new DbContextOptionsBuilder<AtlasDbContext>();
        optionsBuilder.UseNpgsql(DbConnectionString);
        using var context = new AtlasDbContext(optionsBuilder.Options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await PostgreSqlContainer.DisposeAsync();
        await RedisContainer.DisposeAsync();
    }
}
