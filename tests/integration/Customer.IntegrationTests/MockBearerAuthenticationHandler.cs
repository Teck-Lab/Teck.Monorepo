// <copyright file="MockBearerAuthenticationHandler.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Customers.IntegrationTests;

/// <summary>
/// Test-only bearer authentication handler that automatically authenticates every request
/// with a synthetic tenant and subject claim. Used exclusively in integration tests to bypass
/// real JWT validation without modifying Customer.Host production code.
/// This handler lives EXCLUSIVELY in the test assembly and is injected into the customer
/// host via <c>WebApplicationFactory.ConfigureTestServices</c> — it is never compiled
/// into the production customer binary.
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

    /// <summary>
    /// Fixed Keycloak subject ("sub" claim) injected into every authenticated request.
    /// <see cref="Customers.Host.Infrastructure.CustomerIdentityAccessor"/> reads this claim
    /// (falling back to <see cref="ClaimTypes.NameIdentifier"/>) to stamp
    /// <c>Customer.KeycloakSubjectId</c> on creation, so tests assert against this exact value.
    /// </summary>
    internal const string TestSubject = "11111111-1111-1111-1111-111111111111";

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
            // CustomerIdentityAccessor.KeycloakSubjectId reads the "sub" claim via
            // HttpContext.User.FindFirstValue("sub") — this must match exactly for
            // CreateCustomerHandler to stamp the correct Keycloak subject on the new customer.
            new Claim("sub", TestSubject),
            new Claim(ClaimTypes.NameIdentifier, TestSubject),
            new Claim(ClaimTypes.Name, "Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
