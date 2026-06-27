using Orders.Application.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;

namespace Orders.Host.Database;

/// <summary>
/// Registers the order persistence stack: tenant-aware read/write contexts, generic
/// repositories, and the unit of work.
/// </summary>
public static class OrderPersistenceExtensions
{
    /// <summary>
    /// Adds the order read/write contexts, repositories and unit of work to the host.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddOrderPersistence(this WebApplicationBuilder builder)
    {
        var write = builder.Configuration.GetConnectionString("OrderWrite")
            ?? builder.Configuration.GetConnectionString("Default")
            ?? throw new System.InvalidOperationException("Missing 'OrderWrite'/'Default' connection string.");
        var read = builder.Configuration.GetConnectionString("OrderRead") ?? write;

        builder.AddHybridMultiTenantDbContexts<OrderDbContext, OrderReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "order");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(OrderReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(OrderWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<OrderDbContext>(sp.GetRequiredService<OrderDbContext>()));

        return builder;
    }
}
