using FastEndpoints;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Gateway.Public.Edge;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.Hosting;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Guard: mock authentication must never reach Production.
// The MockBearerAuthenticationHandler lives ONLY in the test assembly and is
// injected via WebApplicationFactory.ConfigureTestServices — it is never
// compiled into this binary. This guard is a belt-and-suspenders check so that
// a misconfigured deploy (appsettings override) cannot activate test-only auth.
bool mockAuth = builder.Configuration.GetValue<bool>("Testing:UseMockAuthentication");
if (mockAuth && builder.Environment.IsProduction())
{
    throw new InvalidOperationException(
        "Mock authentication must never be enabled in Production.");
}

// AuthN: JWT bearer from Keycloak, bound from the "Keycloak" config section.
KeycloakAuthenticationOptions keycloak =
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!;
builder.Services.AddKeycloak(builder.Configuration, builder.Environment, keycloak);

// AuthZ: "authenticated" is referenced by YARP route AuthorizationPolicy metadata.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
builder.Services.AddTeckCloudMultiTenancy();

// Edge pipeline: options, registry (fail-closed), FusionCache, token exchange,
// tenant resolver, keyed circuit-breaker, DB-strategy resolver, ordered steps.
builder.AddEdgePipeline();

// AddServiceDefaults() (called above) already registers service discovery.
// AddPassThroughServiceEndpointProvider lets plain DNS names fall through in dev/K8s.
builder.Services.AddPassThroughServiceEndpointProvider();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddEdgeGatewayTransforms(builder.Configuration.GetEdgeTenantOptions());

var app = builder.Build();

// Remote client for the customer tenant-authority service (Phase C gRPC).
// Unreachable at boot is expected in dev; only invoked per-request on authenticated routes.
app.MapRemote(
    builder.Configuration["Services:CustomerApi:Url"]!,
    remote => remote.Register<GetTenantDatabaseInfoCommand, TenantDatabaseInfoRpcResult>());

// UseAuthentication/UseRouting/UseAuthorization BEFORE the YARP endpoint so the
// ClaimsPrincipal is populated and YARP's per-route AuthorizationPolicy rejects
// unauthenticated callers before any tenant/token-exchange logic runs.
// UseRouting must precede UseAuthorization so endpoint metadata is available.
//
// EdgeEnforcementMiddleware runs INSIDE YARP's inner pipeline (via the MapReverseProxy
// callback) so that IReverseProxyFeature is already populated when the middleware executes.
// Placing it in the outer pipeline would mean IReverseProxyFeature is always null and the
// edge steps would never run.
app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();
app.UseMultiTenant();
app.MapReverseProxy(proxy => proxy.UseMiddleware<EdgeEnforcementMiddleware>());
app.MapDefaultEndpoints();

return await app.RunTeckServiceAsync(args);

/// <summary>
/// Entry point for the public-gateway host; exposed as a partial class so
/// integration tests can reference it via <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program { }
