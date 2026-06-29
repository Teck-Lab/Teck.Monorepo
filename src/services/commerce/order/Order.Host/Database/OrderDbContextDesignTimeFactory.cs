using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Orders.Application.Database;

namespace Orders.Host.Database;

/// <summary>
/// Design-time factory for <see cref="OrderDbContext"/> used by EF Core migrations tooling.
/// The factory provides a stub context with a no-op tenant accessor so that
/// <c>dotnet ef migrations add</c> can construct the context without a running application host.
/// </summary>
public sealed class OrderDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    /// <inheritdoc/>
    public OrderDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ORDER_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=order_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<OrderDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(OrderDbContextDesignTimeFactory).Assembly.FullName));

        return new OrderDbContext(optionsBuilder.Options, tenantContextAccessor: null!);
    }
}
