using Grpc.Core;
using Grpc.Net.Client;
using Licensing.Application;
using Licensing.Host;
using Licensing.Host.Grpc.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Licensing.UnitTests;

public sealed class EnterprisePolicyGrpcTests
{
    [Fact]
    public async Task Get_returns_signed_active_policy_for_exact_identity_without_token()
    {
        var store = new InMemoryEnterprisePolicyStore();
        var now = DateTimeOffset.UtcNow;
        var policy = Policy(now);
        store.AddState(policy, EnterprisePolicyStatus.Active);
        await using var app = await CreateAppAsync(store);

        var response = await NewClient(app).GetEnterprisePolicyAsync(Request(policy));

        Assert.Equal(policy.PolicyId, response.Policy.PolicyId);
        Assert.Equal(policy.PolicyVersion, response.Policy.PolicyVersion);
        Assert.Equal(policy.Signature, response.Policy.Signature);
        Assert.Equal(PolicyStatus.Active, response.Status);
    }

    [Theory]
    [InlineData(EnterprisePolicyStatus.Revoked)]
    [InlineData(EnterprisePolicyStatus.Superseded)]
    [InlineData(EnterprisePolicyStatus.NotYetValid)]
    [InlineData(EnterprisePolicyStatus.Expired)]
    public async Task Get_refuses_non_active_policies(EnterprisePolicyStatus status)
    {
        var store = new InMemoryEnterprisePolicyStore();
        var policy = Policy(DateTimeOffset.UtcNow);
        store.AddState(policy, status);
        await using var app = await CreateAppAsync(store);

        var error = await Assert.ThrowsAsync<RpcException>(() => NewClient(app).GetEnterprisePolicyAsync(Request(policy)).ResponseAsync);

        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
    }

    [Fact]
    public async Task Get_does_not_return_policy_for_wrong_tenant_subscription_or_order()
    {
        var store = new InMemoryEnterprisePolicyStore();
        var policy = Policy(DateTimeOffset.UtcNow);
        store.AddState(policy, EnterprisePolicyStatus.Active);
        await using var app = await CreateAppAsync(store);
        var requests = new[]
        {
            new GetEnterprisePolicyRequest { PolicyId = policy.PolicyId, PolicyVersion = policy.PolicyVersion,
                TenantId = Guid.NewGuid().ToString(), SubscriptionId = policy.SubscriptionId.ToString(), SourceOrderId = policy.SourceOrderId.ToString() },
            new GetEnterprisePolicyRequest { PolicyId = policy.PolicyId, PolicyVersion = policy.PolicyVersion,
                TenantId = policy.TenantId.ToString(), SubscriptionId = Guid.NewGuid().ToString(), SourceOrderId = policy.SourceOrderId.ToString() },
            new GetEnterprisePolicyRequest { PolicyId = policy.PolicyId, PolicyVersion = policy.PolicyVersion,
                TenantId = policy.TenantId.ToString(), SubscriptionId = policy.SubscriptionId.ToString(), SourceOrderId = Guid.NewGuid().ToString() }
        };
        foreach (var request in requests)
        {
            var error = await Assert.ThrowsAsync<RpcException>(() => NewClient(app).GetEnterprisePolicyAsync(request).ResponseAsync);
            Assert.Equal(StatusCode.NotFound, error.StatusCode);
        }
    }

    [Fact]
    public async Task Get_returns_not_found_for_missing_policy()
    {
        await using var app = await CreateAppAsync(new InMemoryEnterprisePolicyStore());
        var error = await Assert.ThrowsAsync<RpcException>(() => NewClient(app).GetEnterprisePolicyAsync(new GetEnterprisePolicyRequest
        {
            PolicyId = "missing", PolicyVersion = "1", TenantId = Guid.NewGuid().ToString(),
            SubscriptionId = Guid.NewGuid().ToString(), SourceOrderId = Guid.NewGuid().ToString()
        }).ResponseAsync);
        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    private static Licensing.Application.EnterprisePolicyDocument Policy(DateTimeOffset now) => new($"policy-{Guid.NewGuid():N}", "7",
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 14, 30, now.AddDays(-1), now.AddDays(1), "enterprise", "key-1", "signature");

    private static GetEnterprisePolicyRequest Request(Licensing.Application.EnterprisePolicyDocument policy) => new()
    {
        PolicyId = policy.PolicyId,
        PolicyVersion = policy.PolicyVersion,
        TenantId = policy.TenantId.ToString(),
        SubscriptionId = policy.SubscriptionId.ToString(),
        SourceOrderId = policy.SourceOrderId.ToString()
    };

    private static async Task<WebApplication> CreateAppAsync(InMemoryEnterprisePolicyStore store)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IEnterprisePolicyStore>(store);
        var app = builder.Build();
        app.MapGrpcService<EnterprisePolicyLookupService>();
        await app.StartAsync();
        return app;
    }

    private static EnterprisePolicyLookup.EnterprisePolicyLookupClient NewClient(WebApplication app)
    {
        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = app.GetTestServer().CreateHandler() });
        return new EnterprisePolicyLookup.EnterprisePolicyLookupClient(channel);
    }
}
