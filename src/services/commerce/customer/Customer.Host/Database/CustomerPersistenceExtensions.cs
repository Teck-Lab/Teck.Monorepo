using Customers.Application.Database;
using Finbuckle.MultiTenant.Extensions;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;
using SharedKernel.Infrastructure.MultiTenant;

namespace Customers.Host.Database;

/// <summary>Registers the customer persistence stack (tenant-aware read/write contexts, repos, UoW).</summary>
public static class CustomerPersistenceExtensions
{
    /// <summary>Adds the customer read/write contexts, repositories and unit of work.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddCustomerPersistence(this WebApplicationBuilder builder)
    {
        var write = builder.Configuration.GetConnectionString("CustomerWrite")
            ?? builder.Configuration.GetConnectionString("Default")
            ?? throw new System.InvalidOperationException("Missing 'CustomerWrite'/'Default' connection string.");
        var read = builder.Configuration.GetConnectionString("CustomerRead") ?? write;

        // Register Finbuckle multi-tenant infrastructure so IMultiTenantContextAccessor<TenantDetails>
        // is available to AddHybridMultiTenantDbContexts. The customer service is the global tenant
        // authority (no per-request tenant resolution needed), so no strategy or store is added.
        builder.Services.AddMultiTenant<TenantDetails>();

        builder.AddHybridMultiTenantDbContexts<CustomerDbContext, CustomerReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "customer");

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(CustomerReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(CustomerWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<CustomerDbContext>(sp.GetRequiredService<CustomerDbContext>()));

        return builder;
    }
}
