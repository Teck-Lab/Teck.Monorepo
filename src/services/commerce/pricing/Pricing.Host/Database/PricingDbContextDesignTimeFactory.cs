using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pricing.Application.Database;

namespace Pricing.Host.Database;

/// <summary>Design-time factory for <see cref="PricingDbContext"/> used by EF Core migrations tooling.</summary>
public sealed class PricingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PricingDbContext>
{
    /// <inheritdoc/>
    public PricingDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("PRICING_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pricing_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<PricingDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(PricingDbContextDesignTimeFactory).Assembly.FullName));

        return new PricingDbContext(optionsBuilder.Options, tenantContextAccessor: null!);
    }
}
