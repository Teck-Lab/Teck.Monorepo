using System.Security.Claims;
using Customers.Application.Customers;
using Customers.Application.Database;
using Customers.Application.EventHandlers.IntegrationEvents;
using Customers.Application.Tenants;
using Customers.Host.Database;
using Customers.Host.Grpc.V1;
using Customers.Host.Infrastructure;
using FastEndpoints;
using Keycloak.AuthServices.Authentication;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Events;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Hosting;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTeckService(typeof(Program).Assembly, builder.Configuration);
builder.Services.AddScoped<ICustomerIdentityAccessor, CustomerIdentityAccessor>();
builder.AddCustomerPersistence();
builder.Services.Configure<KeycloakOrganizationOptions>(builder.Configuration.GetSection(KeycloakOrganizationOptions.SectionName));
builder.Services.Configure<TenantPurgeApprovalOptions>(builder.Configuration.GetSection(TenantPurgeApprovalOptions.SectionName));
builder.Services.AddSingleton(_ => new TenantPurgeApprovalSigningKey(
    builder.Configuration["PurgeApproval:EncryptedPrivateKey"], builder.Configuration["PurgeApproval:Passphrase"]));
builder.Services.AddHttpClient<IOrganizationProvisioner, KeycloakOrganizationProvisioner>();
builder.Services.AddSingleton<TenantReadinessVerifier>();
builder.Services.AddSingleton(_ => new SubscriptionPaymentVerificationKeys(
    builder.Configuration.GetSection("PaymentProof:TrustedPublicKeys").Get<string[]>()));
builder.Services.AddKeycloak(builder.Configuration, builder.Environment,
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!);
builder.ConfigureInternalServiceTransport();
builder.AddHandlerServer();
builder.AddTeckMessaging(typeof(CustomerDbContext).Assembly, "CustomerWrite");
builder.Services.AddAuthorization(options => options.AddPolicy("BillingPurchase", policy =>
    policy.RequireAuthenticatedUser().RequireClaim("azp", "billing-api")
        .RequireClaim("billing_service_identity", "billing-api")
        .RequireClaim("aud", "customer-api")
        .RequireAssertion(context => context.User.FindAll("scope")
            .Any(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("tenant-purchase", StringComparer.Ordinal)))));
builder.Services.AddAuthorization(options => options.AddPolicy("PendingTenantOnboarding", policy =>
    policy.RequireAuthenticatedUser().RequireClaim("aud", "customer-api")
        .RequireAssertion(context =>
            !string.IsNullOrWhiteSpace(context.User.FindFirst("sub")?.Value ??
                context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value) &&
            !context.User.HasClaim("billing_service_identity", "billing-api"))));

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

// Customer reserves the tenant identifier before payment. Migrations must be applied
// before reservations are accepted; Keycloak organizations are not created here.
static async Task MigrateDatabaseAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
    await db.Database.MigrateAsync();
}
