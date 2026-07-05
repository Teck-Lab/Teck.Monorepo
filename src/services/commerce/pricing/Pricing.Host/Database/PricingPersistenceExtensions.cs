using Pricing.Application.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;

namespace Pricing.Host.Database;

/// <summary>Registers the pricing persistence stack: tenant-aware contexts, repositories, unit of work.</summary>
public static class PricingPersistenceExtensions
{
    /// <summary>Adds the pricing read/write contexts, repositories and unit of work to the host.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddPricingPersistence(this WebApplicationBuilder builder)
    {
        var write = CodegenConnectionString.ResolveRequired(builder.Configuration, "PricingWrite", "Default");
        var read = builder.Configuration.GetConnectionString("PricingRead") ?? write;

        builder.AddHybridMultiTenantDbContexts<PricingDbContext, PricingReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "pricing");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(PricingReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(PricingWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<PricingDbContext>(sp.GetRequiredService<PricingDbContext>()));

        return builder;
    }
}
