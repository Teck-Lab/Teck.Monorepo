using Customers.Application.Customers;
using Customers.Application.Database;
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
    await MigrateDatabaseAsync(app);
}

return await app.RunTeckServiceAsync(args);

// Local tenant records are provisioned from Keycloak-generated organization identifiers.
// Development startup still applies Customer migrations before the local provisioner runs.
static async Task MigrateDatabaseAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
    await db.Database.MigrateAsync();
}
