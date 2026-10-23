// <copyright file="MockBearerAuthenticationHandler.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gateway.WebAdmin.IntegrationTests;

/// <summary>
/// Test-only bearer authentication handler that authenticates requests based on the
/// <see cref="MockTenantIdHeader"/> request header instead of validating a real JWT.
/// This handler lives EXCLUSIVELY in the test assembly and is injected into the gateway
/// host via <c>WebApplicationFactory.ConfigureTestServices</c> — it is never compiled
/// into the production gateway binary.
/// </summary>
internal sealed class MockBearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The authentication scheme name registered in tests.</summary>
    internal const string SchemeName = "MockBearer";

    /// <summary>
    /// Request header used by the test client to declare which tenant the request is
    /// authenticated as. Absent header → unauthenticated (returns NoResult).
    /// </summary>
    internal const string MockTenantIdHeader = "X-Mock-TenantId";

    /// <summary>Fake inbound bearer token stored in ticket properties so ExchangeTokenStep has something to exchange.</summary>
    private const string FakeInboundToken = "mock-inbound-bearer-token";

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
        string? tenantId = Context.Request.Headers[MockTenantIdHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(tenantId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("tenant_id", tenantId),
            new(ClaimTypes.NameIdentifier, "test-user"),
            new(ClaimTypes.Name, "Test User"),
        };
        if (Context.Request.Headers.ContainsKey("X-Mock-Developer"))
        {
            claims.Add(new Claim(ClaimTypes.Role, "platform-developer"));
        }
        if (Context.Request.Headers.ContainsKey("X-Mock-Manager"))
        {
            claims.Add(new Claim(ClaimTypes.Role, "platform-manager"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);

        // Authentication without a bearer token must not permit a downstream exchange.
        var properties = new AuthenticationProperties();
        if (!Context.Request.Headers.ContainsKey("X-Mock-No-Access-Token"))
        {
            properties.StoreTokens(new[]
            {
                new AuthenticationToken { Name = "access_token", Value = FakeInboundToken },
            });
        }

        var ticket = new AuthenticationTicket(principal, properties, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
