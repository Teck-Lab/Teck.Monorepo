using System.Security.Claims;
using Gateway.Public.Edge;
using Gateway.Public.Edge.Steps;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Infrastructure.Auth;
using Xunit;

namespace Gateway.Public.UnitTests.Edge.Steps;

/// <summary>Unit tests for <see cref="ExchangeTokenStep"/>.</summary>
public sealed class ExchangeTokenStepTests
{
    /// <summary>A successful exchange sets ExchangedToken, the HttpContext item and returns Proceed.</summary>
    [Fact]
    public async Task Success_SetsExchangedTokenAndProceeds()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["Authorization"] = "Bearer inbound-token";
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order-api"))
        {
            ResolvedTenantId = "t1",
        };
        var step = new ExchangeTokenStep(
            new FakeExchangeService(new ServiceTokenResult("exchanged-token", DateTime.UtcNow.AddHours(1))));

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.True(result.Continue);
        Assert.Equal("exchanged-token", ctx.ExchangedToken);
        Assert.Equal("exchanged-token", ctx.HttpContext.Items[EdgeHeaders.ExchangedTokenItemKey]);
    }

    /// <summary>A TokenExchangeException with IsAuthFailure and expired description stops with 401 authorization.token.expired.</summary>
    [Fact]
    public async Task AuthFailure_Expired_Returns401TokenExpired()
    {
        var ex = new TokenExchangeException("Token expired.", "invalid_grant", "Token has expired.", 401, true);
        var http = new DefaultHttpContext();
        http.Request.Headers["Authorization"] = "Bearer old-token";
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order-api"))
        {
            ResolvedTenantId = "t1",
        };
        var step = new ExchangeTokenStep(new FakeExchangeService(ex));

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.False(result.Continue);
        Assert.Equal(401, result.Problem!.StatusCode);
        Assert.Equal("authorization.token.expired", result.Problem.ErrorCode);
        Assert.Equal("Bearer token expired or invalid. Re-authenticate and try again.", result.Problem.Detail);
    }

    /// <summary>A TokenExchangeException with IsAuthFailure and 403 status stops with 403 authorization.token_exchange_denied.</summary>
    [Fact]
    public async Task AuthFailure_Denied_Returns403TokenExchangeDenied()
    {
        var ex = new TokenExchangeException("Access denied.", "access_denied", "Insufficient scope.", 403, true);
        var http = new DefaultHttpContext();
        http.Request.Headers["Authorization"] = "Bearer valid-token";
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order-api"))
        {
            ResolvedTenantId = "t1",
        };
        var step = new ExchangeTokenStep(new FakeExchangeService(ex));

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.False(result.Continue);
        Assert.Equal(403, result.Problem!.StatusCode);
        Assert.Equal("authorization.token_exchange_denied", result.Problem.ErrorCode);
        Assert.Equal("Insufficient scope.", result.Problem.Detail);
    }

    /// <summary>A blank ExchangeAudience short-circuits without calling the exchange service.</summary>
    [Fact]
    public async Task BlankAudience_ProceedsWithoutCallingService()
    {
        var fake = new CountingFakeExchangeService();
        var http = new DefaultHttpContext();
        http.Request.Headers["Authorization"] = "Bearer some-token";
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Anonymous, null));
        var step = new ExchangeTokenStep(fake);

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.True(result.Continue);
        Assert.Equal(0, fake.CallCount);
    }

    /// <summary>When the exchange service returns a null or blank access token, the step stops with 401 authorization.token_exchange_denied.</summary>
    [Fact]
    public async Task BlankExchangedToken_Returns401TokenExchangeDenied()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["Authorization"] = "Bearer inbound-token";
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order-api"))
        {
            ResolvedTenantId = "t1",
        };
        var step = new ExchangeTokenStep(
            new FakeExchangeService(new ServiceTokenResult(string.Empty, DateTime.UtcNow.AddHours(1))));

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.False(result.Continue);
        Assert.Equal(401, result.Problem!.StatusCode);
        Assert.Equal("authorization.token_exchange_denied", result.Problem.ErrorCode);
        Assert.Equal("Token exchange returned an empty access token.", result.Problem.Detail);
        Assert.Null(ctx.ExchangedToken);
    }

    /// <summary>No inbound bearer token short-circuits without calling the exchange service.</summary>
    [Fact]
    public async Task NoInboundToken_ProceedsWithoutCallingService()
    {
        var fake = new CountingFakeExchangeService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService, FakeAuthenticationService>();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order-api"))
        {
            ResolvedTenantId = "t1",
        };
        var step = new ExchangeTokenStep(fake);

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.True(result.Continue);
        Assert.Equal(0, fake.CallCount);
    }

    private sealed class FakeExchangeService : IServiceTokenExchangeService
    {
        private readonly ServiceTokenResult? result;
        private readonly Exception? exception;

        public FakeExchangeService(ServiceTokenResult result) => this.result = result;

        public FakeExchangeService(Exception exception) => this.exception = exception;

        public Task<ServiceTokenResult> ExchangeTokenAsync(
            string subjectToken,
            string audience,
            string contextKey,
            CancellationToken cancellationToken = default)
        {
            if (this.exception is not null)
            {
                throw this.exception;
            }

            return Task.FromResult(this.result!);
        }
    }

    private sealed class CountingFakeExchangeService : IServiceTokenExchangeService
    {
        public int CallCount { get; private set; }

        public Task<ServiceTokenResult> ExchangeTokenAsync(
            string subjectToken,
            string audience,
            string contextKey,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ServiceTokenResult("fallback-token", DateTime.UtcNow.AddHours(1)));
        }
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();
    }
}
