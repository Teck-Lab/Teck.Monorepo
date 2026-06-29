using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using SharedKernel.Infrastructure.Endpoints;
using SharedKernel.Infrastructure.OpenApi;
using Xunit;

namespace SharedKernel.UnitTests.Endpoints;

/// <summary>
/// Tests for <see cref="AuthenticatedEndpoint{TRequest,TResponse}"/> — verifies that
/// <see cref="AuthenticatedEndpoint{TRequest,TResponse}.Configure"/> correctly wires audience
/// metadata and authorization state based on the declared <see cref="EndpointPermission"/>.
/// </summary>
public sealed class AuthenticatedEndpointTests
{
    private sealed class ProtectedTestEndpoint : AuthenticatedEndpoint<EmptyRequest, EmptyResponse>
    {
        protected override EndpointPermission Permission => new("order", "create", "public");

        protected override void ConfigureEndpoint() => Post("/test/protected");

        public override Task HandleAsync(EmptyRequest req, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AnonymousTestEndpoint : AuthenticatedEndpoint<EmptyRequest, EmptyResponse>
    {
        protected override EndpointPermission Permission => EndpointPermission.Anonymous("public");

        protected override void ConfigureEndpoint() => Get("/test/anon");

        public override Task HandleAsync(EmptyRequest req, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Verifies that a protected endpoint adds <see cref="OpenApiAudienceMetadata"/> to
    /// <c>EndpointMetadata</c>, does not allow anonymous access, and registers the JWT bearer
    /// authentication scheme.
    /// </summary>
    [Fact]
    public void ProtectedEndpoint_TagsAudience_AndIsNotAnonymous()
    {
        var ep = Factory.Create<ProtectedTestEndpoint>();

        Assert.Contains(ep.Definition.EndpointMetadata ?? [], m => m is OpenApiAudienceMetadata a && a.Audiences.Contains("public"));
        Assert.Null(ep.Definition.AnonymousVerbs);
        Assert.Contains(JwtBearerDefaults.AuthenticationScheme, ep.Definition.AuthSchemeNames ?? []);
    }

    /// <summary>
    /// Verifies that an anonymous endpoint adds <see cref="OpenApiAudienceMetadata"/> to
    /// <c>EndpointMetadata</c> and allows anonymous access on all verbs.
    /// </summary>
    [Fact]
    public void AnonymousEndpoint_TagsAudience_AndAllowsAnonymous()
    {
        var ep = Factory.Create<AnonymousTestEndpoint>();

        Assert.Contains(ep.Definition.EndpointMetadata ?? [], m => m is OpenApiAudienceMetadata a && a.Audiences.Contains("public"));
        Assert.NotNull(ep.Definition.AnonymousVerbs);
    }
}
