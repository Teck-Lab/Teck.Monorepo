// <copyright file="MockBearerAuthenticationHandler.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catalog.IntegrationTests;

/// <summary>
/// Test-only bearer authentication handler that automatically authenticates every request
/// with a synthetic tenant and customer claim. Used exclusively in integration tests to bypass
/// real JWT validation without modifying Basket.Host production code.
/// This handler lives EXCLUSIVELY in the test assembly and is injected into the basket
/// host via <c>WebApplicationFactory.ConfigureTestServices</c> — it is never compiled
/// into the production basket binary.
/// Registered under <see cref="JwtBearerDefaults.AuthenticationScheme"/> ("Bearer") so that
/// <c>AuthSchemes(JwtBearerDefaults.AuthenticationScheme)</c> in <see cref="SharedKernel.Infrastructure.Endpoints.AuthenticatedEndpoint{TRequest,TResponse}"/>
/// correctly routes authentication to this test handler.
/// </summary>
internal sealed class MockBearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// The authentication scheme name — matches <see cref="JwtBearerDefaults.AuthenticationScheme"/> ("Bearer")
    /// so FastEndpoints' <c>AuthSchemes("Bearer")</c> call resolves this test handler.
    /// </summary>
    internal const string SchemeName = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>Fixed tenant id injected into every authenticated request.</summary>
    internal const string TestTenantId = "00000000-0000-0000-0000-000000000001";

    /// <summary>Fixed customer id injected into every authenticated request.</summary>
    internal const string TestCustomerId = "00000000-0000-0000-0000-000000000002";

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

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tenantId = Context.Request.Headers.TryGetValue("X-Test-Tenant-Id", out var requestedTenantId)
            ? requestedTenantId.ToString()
            : TestTenantId;
        var claims = new List<Claim>();
        if (!string.Equals(tenantId, "none", StringComparison.Ordinal))
        {
            claims.Add(new Claim("tenant_id", tenantId));
        }

        claims.Add(new Claim("customer_id", TestCustomerId));
        claims.Add(new Claim(ClaimTypes.NameIdentifier, "test-user"));
        claims.Add(new Claim(ClaimTypes.Name, "Test User"));
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
