using Inventories.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventories.Host.Database;

/// <summary>
/// Design-time factory for <see cref="InventoryDbContext"/> used by EF Core migrations tooling.
/// The factory provides a stub context with a no-op tenant accessor so that
/// <c>dotnet ef migrations add</c> can construct the context without a running application host.
/// </summary>
public sealed class InventoryDbContextDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    /// <inheritdoc/>
    public InventoryDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("INVENTORY_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=inventory_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(InventoryDbContextDesignTimeFactory).Assembly.FullName));

        return new InventoryDbContext(optionsBuilder.Options, tenantContextAccessor: null!);
    }
}
