using FastEndpoints;
using Gateway.Public.Edge;
using Keycloak.AuthServices.Authentication;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

// AuthN: JWT bearer from Keycloak, bound from the "Keycloak" config section.
KeycloakAuthenticationOptions keycloak =
    builder.Configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!;
builder.Services.AddKeycloak(builder.Configuration, builder.Environment, keycloak);

// AuthZ: "authenticated" is referenced by YARP route AuthorizationPolicy metadata.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());

// Edge pipeline: options, registry (fail-closed), FusionCache, token exchange,
// tenant resolver, keyed circuit-breaker, DB-strategy resolver, ordered steps.
builder.AddEdgePipeline();

// Service discovery: passthrough is correct here because YARP cluster addresses are
// plain DNS names (resolved by K8s DNS in production; falls through in dev).
builder.Services.AddServiceDiscovery();
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

// UseAuthentication/UseRouting/UseAuthorization BEFORE the edge middleware so the
// ClaimsPrincipal is populated and YARP's per-route AuthorizationPolicy rejects
// unauthenticated callers before any tenant/token-exchange logic runs.
// UseRouting must precede UseAuthorization so endpoint metadata is available.
app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();
app.UseMiddleware<EdgeEnforcementMiddleware>();
app.MapReverseProxy();

await app.RunAsync();

/// <summary>
/// Entry point for the public-gateway host; exposed as a partial class so
/// integration tests can reference it via <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program { }
