// <copyright file="DeterministicBearerToken.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CrossService.IntegrationTests;

/// <summary>Issues deterministic, signed tokens for the production-host lifecycle seam.</summary>
internal static class DeterministicBearerToken
{
    internal const string TenantId = "00000000-0000-0000-0000-000000000001";
    internal const string Subject = "test-user";
    internal const string IssuerBaseUrl = "http://keycloak.test";
    internal const string Issuer = IssuerBaseUrl + "/realms/test";

    internal static SymmetricSecurityKey SigningKey { get; } = new(Encoding.UTF8.GetBytes("cross-service-test-signing-key-32b"));

    internal static string Issue(string audience, string? subject = Subject, string tenantId = TenantId)
    {
        var claims = new List<Claim> { new("tenant_id", tenantId) };
        if (!string.IsNullOrWhiteSpace(subject))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));
        }

        var token = new JwtSecurityToken(
            Issuer,
            audience,
            claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>Legacy focused-handler test authentication seam; never used by production-host facts.</summary>
internal sealed class MockBearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    internal const string SchemeName = JwtBearerDefaults.AuthenticationScheme;
    internal const string TestTenantId = DeterministicBearerToken.TenantId;

    public MockBearerAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant_id", TestTenantId), new Claim("sub", DeterministicBearerToken.Subject)], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
