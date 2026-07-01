using Baskets.Application.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;

namespace Baskets.Host.Database;

/// <summary>
/// Registers the basket persistence stack: tenant-aware read/write contexts, generic
/// repositories, and the unit of work.
/// </summary>
public static class BasketPersistenceExtensions
{
    /// <summary>Adds the basket read/write contexts, repositories and unit of work to the host.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddBasketPersistence(this WebApplicationBuilder builder)
    {
        var write = CodegenConnectionString.ResolveRequired(builder.Configuration, "BasketWrite", "Default");
        var read = builder.Configuration.GetConnectionString("BasketRead") ?? write;

        builder.AddHybridMultiTenantDbContexts<BasketDbContext, BasketReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "basket");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(BasketReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(BasketWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<BasketDbContext>(sp.GetRequiredService<BasketDbContext>()));

        return builder;
    }
}
