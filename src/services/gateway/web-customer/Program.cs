using FastEndpoints;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Gateway.Edge;
using Gateway.Edge.Contracts;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

if (builder.Environment.IsProduction() && builder.Configuration.GetValue<bool>("Testing:UseMockAuthentication"))
{
    throw new InvalidOperationException("Mock authentication must never be enabled in Production.");
}

KeycloakAuthenticationOptions keycloak =
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!;
builder.Services.AddKeycloak(builder.Configuration, builder.Environment, keycloak);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
builder.Services.AddTeckCloudMultiTenancy();
builder.AddEdgePipeline();
builder.Services.AddPassThroughServiceEndpointProvider();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddEdgeGatewayTransforms(builder.Configuration.GetEdgeTenantOptions());

var app = builder.Build();
app.MapRemote(
    builder.Configuration["Services:CustomerApi:Url"]!,
    remote => remote.Register<GetTenantDatabaseInfoCommand, TenantDatabaseInfoRpcResult>());
app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();
app.UseMultiTenant();
app.MapReverseProxy(proxy => proxy.UseMiddleware<EdgeEnforcementMiddleware>());
app.MapGatewayOpenApi("customer");
app.MapDefaultEndpoints();

app.Run();

/// <summary>Entry point for the customer-facing YARP gateway.</summary>
public partial class Program { }
