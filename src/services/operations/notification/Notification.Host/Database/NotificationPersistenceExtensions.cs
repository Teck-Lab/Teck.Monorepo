using Notifications.Application.Database;
using SharedKernel.Core.Database;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.MultiTenant;

namespace Notifications.Host.Database;

/// <summary>Registers tenant-aware notification persistence and generic repository services.</summary>
public static class NotificationPersistenceExtensions
{
    /// <summary>Adds notification read and write contexts, repositories, and unit of work.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddNotificationPersistence(this WebApplicationBuilder builder)
    {
        var write = CodegenConnectionString.ResolveRequired(builder.Configuration, "NotificationWrite", "Default");
        var read = builder.Configuration.GetConnectionString("NotificationRead") ?? write;
        builder.AddHybridMultiTenantDbContexts<NotificationDbContext, NotificationReadDbContext>(
            migrationsAssembly: typeof(Program).Assembly,
            defaultWriteConnectionString: write,
            defaultReadConnectionString: read,
            serviceName: "notification");
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(typeof(IGenericReadRepository<,>), typeof(NotificationReadRepository<,>));
        builder.Services.AddScoped(typeof(IGenericWriteRepository<,>), typeof(NotificationWriteRepository<,>));
        builder.Services.AddScoped<IUnitOfWork>(sp => new UnitOfWork<NotificationDbContext>(sp.GetRequiredService<NotificationDbContext>()));
        return builder;
    }
}
