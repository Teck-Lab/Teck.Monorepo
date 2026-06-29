using Customers.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Customers.Host.Database;

/// <summary>
/// Design-time factory for <see cref="CustomerDbContext"/> used by EF Core migrations tooling.
/// The factory provides a stub context with a no-op tenant accessor so that
/// <c>dotnet ef migrations add</c> can construct the context without a running application host.
/// </summary>
public sealed class CustomerDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CustomerDbContext>
{
    /// <inheritdoc/>
    public CustomerDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("CUSTOMER_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=customer_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<CustomerDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(CustomerDbContextDesignTimeFactory).Assembly.FullName));

        return new CustomerDbContext(optionsBuilder.Options, tenantContextAccessor: null!);
    }
}
