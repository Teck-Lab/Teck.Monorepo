extern alias LicensingHost;

using System.Security.Cryptography;
using System.Text.Json;
using Grpc.Core;
using Licensing.Host.Grpc.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Events;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Billing.IntegrationTests;

public sealed class EnterprisePolicyFreshnessVerifierTests
{
    [Theory]
    [InlineData(PolicyStatus.Active, false, false)]
    [InlineData(PolicyStatus.Revoked, false, true)]
    [InlineData(PolicyStatus.Superseded, false, true)]
    [InlineData(PolicyStatus.Active, true, true)]
    public async Task Verifier_uses_exact_identity_and_only_accepts_active_matching_document(
        PolicyStatus status, bool mismatch, bool rejects)
    {
        using var signingKey = RSA.Create(2048);
        using var verificationKeys = new EnterprisePolicyDocumentVerificationKeys(
            [new KeyValuePair<string, string?>("license-key", signingKey.ExportSubjectPublicKeyInfoPem())]);
        var now = DateTimeOffset.UtcNow;
        var transition = new EnterprisePolicyAttachedIntegrationEvent
        {
            TenantId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), SourceOrderId = Guid.NewGuid(),
            PolicyId = "policy-7", PolicyVersion = "v3", GracePeriodDays = 5, BackupRetentionDays = 21,
            LicenseKind = "custom_enterprise", LicensingSigningKeyId = "license-key",
            ValidFromUtc = now.AddMinutes(-1), ValidUntilUtc = now.AddDays(10),
        };
        transition.LicensingSignature = SignDocument(transition, signingKey);
        var request = new TaskCompletionSource<GetEnterprisePolicyRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var responsePolicy = new EnterprisePolicyDocument
        {
            PolicyId = transition.PolicyId, PolicyVersion = transition.PolicyVersion,
            TenantId = mismatch ? Guid.NewGuid().ToString("D") : transition.TenantId.ToString("D"),
            SubscriptionId = transition.SubscriptionId.ToString("D"), SourceOrderId = transition.SourceOrderId.ToString("D"),
            GracePeriodDays = transition.GracePeriodDays, BackupRetentionDays = transition.BackupRetentionDays,
            ValidFromUtc = transition.ValidFromUtc.ToString("O"), ValidUntilUtc = transition.ValidUntilUtc.ToString("O"),
            LicenseKind = transition.LicenseKind, SigningKeyId = transition.LicensingSigningKeyId,
            Signature = transition.LicensingSignature,
        };
        await using var server = await StartServer(async (req, _) =>
        {
            request.TrySetResult(req);
            if (status == PolicyStatus.Revoked || status == PolicyStatus.Superseded)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "not active"));
            return new GetEnterprisePolicyResponse { Status = status, Policy = responsePolicy };
        });

        var verifier = new EnterprisePolicyFreshnessVerifier(
            Options.Create(new EnterprisePolicyOptions { Address = server.Address }), verificationKeys);
        if (rejects)
            await Assert.ThrowsAnyAsync<Exception>(() => verifier.VerifyAsync(transition, CancellationToken.None));
        else
            await verifier.VerifyAsync(transition, CancellationToken.None);

        var actualRequest = await request.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(transition.PolicyId, actualRequest.PolicyId);
        Assert.Equal(transition.PolicyVersion, actualRequest.PolicyVersion);
        Assert.Equal(transition.TenantId.ToString("D"), actualRequest.TenantId);
        Assert.Equal(transition.SubscriptionId.ToString("D"), actualRequest.SubscriptionId);
        Assert.Equal(transition.SourceOrderId.ToString("D"), actualRequest.SourceOrderId);
    }

    [Fact]
    public async Task Verifier_fails_closed_when_licensing_is_unavailable()
    {
        using var signingKey = RSA.Create(2048);
        using var verificationKeys = new EnterprisePolicyDocumentVerificationKeys(
            [new KeyValuePair<string, string?>("license-key", signingKey.ExportSubjectPublicKeyInfoPem())]);
        var transition = new EnterprisePolicyAttachedIntegrationEvent
        {
            TenantId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), SourceOrderId = Guid.NewGuid(),
            PolicyId = "policy-7", PolicyVersion = "v3",
        };
        await using var server = await StartServer((_, _) => throw new RpcException(new Status(StatusCode.Unavailable, "offline")));
        var verifier = new EnterprisePolicyFreshnessVerifier(
            Options.Create(new EnterprisePolicyOptions { Address = server.Address }), verificationKeys);
        await Assert.ThrowsAsync<RpcException>(() => verifier.VerifyAsync(transition, CancellationToken.None));
    }

    private static async Task<LocalGrpcServer> StartServer(
        Func<GetEnterprisePolicyRequest, ServerCallContext, Task<GetEnterprisePolicyResponse>> handler)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(new LookupService(handler));
        var app = builder.Build();
        app.MapGrpcService<LookupService>();
        app.Services.GetRequiredService<LookupService>();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.Single();
        return new LocalGrpcServer(app, address);
    }

    private static string SignDocument(EnterprisePolicyAttachedIntegrationEvent transition, RSA key)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            policy_id = transition.PolicyId, policy_version = transition.PolicyVersion,
            tenant_id = transition.TenantId, subscription_id = transition.SubscriptionId,
            source_order_id = transition.SourceOrderId, grace_period_days = transition.GracePeriodDays,
            backup_retention_days = transition.BackupRetentionDays,
            valid_from_utc = transition.ValidFromUtc.ToUniversalTime().ToString("O"),
            valid_until_utc = transition.ValidUntilUtc.ToUniversalTime().ToString("O"),
            license_kind = transition.LicenseKind, signing_key_id = transition.LicensingSigningKeyId,
        });
        return Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }

    private sealed class LookupService(
        Func<GetEnterprisePolicyRequest, ServerCallContext, Task<GetEnterprisePolicyResponse>> handler)
        : EnterprisePolicyLookup.EnterprisePolicyLookupBase
    {
        public override Task<GetEnterprisePolicyResponse> GetEnterprisePolicy(GetEnterprisePolicyRequest request, ServerCallContext context) => handler(request, context);
    }

    private sealed class LocalGrpcServer(WebApplication app, string address) : IAsyncDisposable
    {
        public string Address { get; } = address;
        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }
}
