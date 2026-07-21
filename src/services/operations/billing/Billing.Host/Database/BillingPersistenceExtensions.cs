using Billings.Application.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;

namespace Billings.Host.Database;

/// <summary>
/// Registers the billing persistence stack: tenant-aware read/write contexts, generic
/// repositories, and the unit of work.
/// </summary>
public static class BillingPersistenceExtensions
{
    /// <summary>Adds the billing read/write contexts, repositories and unit of work to the host.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddBillingPersistence(this WebApplicationBuilder builder)
    {
        var write = CodegenConnectionString.ResolveRequired(builder.Configuration, "BillingWrite", "Default");
        var read = builder.Configuration.GetConnectionString("BillingRead") ?? write;

        builder.AddHybridMultiTenantDbContexts<BillingDbContext, BillingReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "billing");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(BillingReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(BillingWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<BillingDbContext>(sp.GetRequiredService<BillingDbContext>()));

        return builder;
    }
}
