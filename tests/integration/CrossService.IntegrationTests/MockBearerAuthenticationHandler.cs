// <copyright file="MockBearerAuthenticationHandler.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrossService.IntegrationTests;

/// <summary>
/// Test-only bearer authentication handler that automatically authenticates every request
/// with a synthetic tenant claim. Injected into both hosts via
/// <c>WebApplicationFactory.ConfigureTestServices</c> so cross-service tests never need a real
/// Keycloak server. The fixed tenant matches across both hosts so the order's tenant and the
/// inventory stock's tenant line up.
/// </summary>
internal sealed class MockBearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// The authentication scheme name — matches <see cref="JwtBearerDefaults.AuthenticationScheme"/> ("Bearer")
    /// so <c>AuthSchemes("Bearer")</c> resolves this test handler.
    /// </summary>
    internal const string SchemeName = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>Fixed tenant id injected into every authenticated request.</summary>
    internal const string TestTenantId = "00000000-0000-0000-0000-000000000001";

    /// <summary>
    /// Initializes a new instance of the <see cref="MockBearerAuthenticationHandler"/> class.
    /// </summary>
    /// <param name="options">The options monitor.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    public MockBearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("tenant_id", TestTenantId),
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, "Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
