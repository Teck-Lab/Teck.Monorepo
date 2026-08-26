using Customers.Application.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database;
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
        var write = CodegenConnectionString.ResolveRequired(builder.Configuration, "CustomerWrite", "Default");
        var read = builder.Configuration.GetConnectionString("CustomerRead") ?? write;

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
