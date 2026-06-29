using Catalog.Application.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;

namespace Catalog.Host.Database;

/// <summary>
/// Registers the catalog persistence stack: tenant-aware read/write contexts, generic
/// repositories, and the unit of work.
/// </summary>
public static class CatalogPersistenceExtensions
{
    /// <summary>
    /// Adds the catalog read/write contexts, repositories and unit of work to the host.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddCatalogPersistence(this WebApplicationBuilder builder)
    {
        var write = CodegenConnectionString.ResolveRequired(builder.Configuration, "CatalogWrite", "Default");
        var read = builder.Configuration.GetConnectionString("CatalogRead") ?? write;

        builder.AddHybridMultiTenantDbContexts<CatalogDbContext, CatalogReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "catalog");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(CatalogReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(CatalogWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<CatalogDbContext>(sp.GetRequiredService<CatalogDbContext>()));

        return builder;
    }
}
