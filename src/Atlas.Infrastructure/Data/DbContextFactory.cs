using Atlas.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace Atlas.Infrastructure.Data;

/// <summary>
/// Used by EF Core tools (dotnet ef migrations add) for design-time DbContext creation.
/// </summary>
public class DbContextFactory : IDesignTimeDbContextFactory<AtlasDbContext>
{
    public AtlasDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connStr = config.GetConnectionString("DefaultConnection")
                      ?? "Host=localhost;Database=atlas;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AtlasDbContext>();
        optionsBuilder.UseNpgsql(connStr);

        return new AtlasDbContext(optionsBuilder.Options);
    }
}
