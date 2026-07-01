using Baskets.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Baskets.Host.Database;

/// <summary>
/// Design-time factory for <see cref="BasketDbContext"/> used by EF Core migrations tooling.
/// The factory provides a stub context with a no-op tenant accessor so that
/// <c>dotnet ef migrations add</c> can construct the context without a running application host.
/// </summary>
public sealed class BasketDbContextDesignTimeFactory : IDesignTimeDbContextFactory<BasketDbContext>
{
    /// <inheritdoc/>
    public BasketDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("BASKET_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=basket_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<BasketDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(BasketDbContextDesignTimeFactory).Assembly.FullName));

        return new BasketDbContext(optionsBuilder.Options, tenantContextAccessor: null!);
    }
}
