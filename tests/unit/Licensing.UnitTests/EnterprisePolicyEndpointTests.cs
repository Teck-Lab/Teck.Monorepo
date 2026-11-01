using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Licensing.Application;
using Licensing.Host;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Licensing.UnitTests;

public sealed class EnterprisePolicyEndpointTests
{
    [Fact]
    public async Task Get_requires_operator_claims_and_returns_existing_policy()
    {
        using var key = RSA.Create(2048);
        var store = new InMemoryEnterprisePolicyStore();
        var issuer = new EnterprisePolicyIssuer(store, key, "key-1");
        var now = DateTimeOffset.UtcNow;
        var policyId = $"policy-{Guid.NewGuid():N}";
        var policy = await issuer.IssueAsync(
            new EnterprisePolicyOperator("human-subject", "licensing-api",
                new HashSet<string>(["licensing:enterprise-policy:issue"]),
                new HashSet<string>(["licensing-enterprise-policy-issuer"])),
            new IssueEnterprisePolicyRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), policyId, "1", 12, 45, now, now.AddYears(1)),
            now, CancellationToken.None);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(issuer);
        builder.Services.AddControllers().AddApplicationPart(typeof(EnterprisePolicyEndpoints).Assembly);
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("test", _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy("EnterprisePolicyIssuer", policy => policy.RequireAuthenticatedUser()
            .RequireClaim("aud", "licensing-api").RequireClaim("scope", "licensing:enterprise-policy:issue")
            .RequireRole("licensing-enterprise-policy-issuer").RequireClaim("sub")));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var path = new Uri($"http://localhost/enterprise-policies/{policyId}/1");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Auth", "wrong");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Auth");
        client.DefaultRequestHeaders.Add("X-Auth", "operator");
        var response = await client.GetAsync(path);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(policyId, (await response.Content.ReadFromJsonAsync<EnterprisePolicyDocument>())?.PolicyId);
        await app.StopAsync();
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var value = Request.Headers["X-Auth"].ToString();
            if (string.IsNullOrEmpty(value)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = value == "operator"
                ? new[] { new Claim("sub", "human-subject"), new Claim("aud", "licensing-api"), new Claim("scope", "licensing:enterprise-policy:issue"), new Claim(ClaimTypes.Role, "licensing-enterprise-policy-issuer") }
                : new[] { new Claim("sub", "human-subject") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
