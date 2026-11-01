using Licensing.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Licensing.Host.Database;

/// <summary>Creates the licensing migration context without starting the authenticated host.</summary>
public sealed class LicensingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LicensingDbContext>
{
    /// <inheritdoc />
    public LicensingDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("LICENSING_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Database=licensing_design;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsAssembly(typeof(LicensingDbContextDesignTimeFactory).Assembly.FullName))
            .Options;
        return new LicensingDbContext(options);
    }
}
