using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Infrastructure.Data;

public class DbContextFactory : IDesignTimeDbContextFactory<AtlasDbContext>
{
    public AtlasDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AtlasDbContext>();
        // Using a standard default connection string for local development / migration generation
        var connectionString = "Host=localhost;Database=atlas;Username=postgres;Password=postgres";
        optionsBuilder.UseNpgsql(connectionString);

        return new AtlasDbContext(optionsBuilder.Options);
    }
}
