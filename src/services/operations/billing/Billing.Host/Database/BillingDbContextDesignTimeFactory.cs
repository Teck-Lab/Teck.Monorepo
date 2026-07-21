using Billings.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Billings.Host.Database;

/// <summary>
/// Design-time factory for <see cref="BillingDbContext"/> used by EF Core migrations tooling.
/// The factory provides a stub context with a no-op tenant accessor so that
/// <c>dotnet ef migrations add</c> can construct the context without a running application host.
/// </summary>
public sealed class BillingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    /// <inheritdoc/>
    public BillingDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("BILLING_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=billing_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<BillingDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(BillingDbContextDesignTimeFactory).Assembly.FullName));

        return new BillingDbContext(optionsBuilder.Options, tenantContextAccessor: null!);
    }
}
