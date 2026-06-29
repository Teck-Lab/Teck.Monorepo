using Customers.Application.Database;
using Customers.Domain.Entities;
using Customers.Host.Database;
using Customers.Host.Grpc.V1;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure;
using SharedKernel.Infrastructure.Behaviors;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.Messaging.DeadLetter;
using SharedKernel.Infrastructure.Observability;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.AddTeckCloudObservability();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.AddCustomerPersistence();
builder.ConfigureInternalServiceTransport();
builder.AddHandlerServer();
builder.Host.UseWolverine(opts =>
{
    opts.AddTeckBehaviors();
    opts.AddTeckDeadLetterPolicy(new DeadLetterOptions());
});

var app = builder.Build();
app.UseTeckService();
app.MapHandlers(registry =>
    registry.Register<GetTenantDatabaseInfoCommand, GetTenantDatabaseInfoCommandHandler, TenantDatabaseInfoRpcResult>());

if (app.Environment.IsDevelopment())
{
    await SeedDevTenantAsync(app);
}

app.Run();

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
