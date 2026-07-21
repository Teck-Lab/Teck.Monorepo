using Customers.Application.Customers;
using Customers.Application.Database;
using Customers.Domain.Entities;
using Customers.Host.Database;
using Customers.Host.Grpc.V1;
using Customers.Host.Infrastructure;
using FastEndpoints;
using Keycloak.AuthServices.Authentication;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Hosting;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddCustomerPersistence();
builder.Services.AddScoped<ICustomerIdentityAccessor, CustomerIdentityAccessor>();
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.ConfigureInternalServiceTransport();
builder.AddHandlerServer();
builder.AddTeckMessaging(typeof(CustomerDbContext).Assembly, "CustomerWrite");

var app = builder.Build();
app.UseTeckService();
app.MapDefaultEndpoints();
app.MapHandlers(registry =>
    registry.Register<GetTenantDatabaseInfoCommand, GetTenantDatabaseInfoCommandHandler, TenantDatabaseInfoRpcResult>());

// Skip dev seeding when the process is started for a build-time command such as
// `codegen write`; that path must not touch the database.
if (!CodeGenerationDetector.IsRunningGeneration() && app.Environment.IsDevelopment())
{
    await SeedDevTenantAsync(app);
}

return await app.RunTeckServiceAsync(args);

// Idempotently inserts the canonical dev tenant in the Development environment.
// This seed does not run in staging or production — it exists for local development
// convenience only and must never be relied on by any integration test fixture.
static async Task SeedDevTenantAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
    await db.Database.MigrateAsync();

    var devTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    bool exists = await db.Set<Tenant>().AnyAsync(t => t.Id == devTenantId);
    if (!exists)
    {
        db.Set<Tenant>().Add(Tenant.Create(
            devTenantId,
            identifier: "dev",
            databaseStrategy: "shared",
            databaseProvider: "postgres",
            hasReadReplicas: false));
        await db.SaveChangesAsync();
    }
}
