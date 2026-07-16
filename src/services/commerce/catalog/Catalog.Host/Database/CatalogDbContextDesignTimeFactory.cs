using Catalog.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Host.Database;

/// <summary>
/// Design-time factory for <see cref="CatalogDbContext"/> used by EF Core migrations tooling.
/// Provides a stub context with a no-op tenant accessor so <c>dotnet ef migrations add</c>
/// can construct the context without a running application host.
/// </summary>
public sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    /// <inheritdoc/>
    public CatalogDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CATALOG_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=catalog_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(CatalogDbContextDesignTimeFactory).Assembly.FullName));

        return new CatalogDbContext(optionsBuilder.Options, tenantContextAccessor: null!);
    }
}
