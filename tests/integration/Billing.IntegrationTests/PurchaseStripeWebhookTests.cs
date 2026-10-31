extern alias LicensingHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Licensing.Application;
using Licensing.Application.Database;
using MemoryPack;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using SharedKernel.Core.Pricing;
using SharedKernel.Events;
using Standard.Licensing.Security.Cryptography;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Exercises the real HTTP signature and PostgreSQL confirmation boundary without a live card.</summary>
[Collection("SharedTestcontainers")]
public sealed class PurchaseStripeWebhookTests(SharedTestcontainersFixture fixture) : BillingIntegrationTestBase(fixture, useRabbitMq: true)
{
    [Fact]
    public async Task SignedSucceededIntent_ConfirmsOnlyExactPendingOrder_RejectsTamperingAndReplay()
    {
        _ = Client;
        _ = WolverineHost;
        var licenseDb = await fixture.CreateSharedTestDatabaseAsync(typeof(LicensingDbContext), "Licensing.Host").ConfigureAwait(false);
        var signingPair = KeyGenerator.Create().GenerateKeyPair();
        const string signingPassphrase = "test-only-billing-license-integration-passphrase";
        const string proofKey = "dGVzdC1saWNlbnNlLXByb29mLWtleS0zMi1ieXRlcy1taW5pbXVt";
        using var licensingFactory = new WebApplicationFactory<LicensingHost::Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Development");
            web.UseSetting("ConnectionStrings:LicensingWrite", licenseDb);
            web.UseSetting("ConnectionStrings:Default", licenseDb);
            web.UseSetting("ConnectionStrings:rabbitmq", RabbitMqConnectionString);
            web.UseSetting("LicenseSigning:KeyId", "test-only-key");
            web.UseSetting("LicenseSigning:EncryptedPrivateKey", signingPair.ToEncryptedPrivateKeyString(signingPassphrase));
            web.UseSetting("LicenseSigning:Passphrase", signingPassphrase);
            web.UseSetting("LicenseSigning:TrustedPublicKeys:0", signingPair.ToPublicKeyString());
            web.UseSetting("Readiness:LicenseKey", proofKey);
            web.UseSetting("PaymentProof:TrustedPublicKeys:0", BillingPaymentPublicKey);
        });
        using var licensingClient = licensingFactory.CreateClient();
        var rabbitFactory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString, UriKind.Absolute) };
        await using var rabbit = await rabbitFactory.CreateConnectionAsync().ConfigureAwait(false);
        await using var channel = await rabbit.CreateChannelAsync().ConfigureAwait(false);
        const string exchange = "SharedKernel.Events.SubscriptionPaymentConfirmedIntegrationEvent";
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true).ConfigureAwait(false);
        var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true).ConfigureAwait(false);
        await channel.QueueBindAsync(queue.QueueName, exchange, string.Empty).ConfigureAwait(false);
        const string readinessExchange = "SharedKernel.Events.TenantProvisioningReadyIntegrationEvent";
        await channel.ExchangeDeclareAsync(readinessExchange, ExchangeType.Fanout, durable: true).ConfigureAwait(false);
        var readinessQueue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true).ConfigureAwait(false);
        await channel.QueueBindAsync(readinessQueue.QueueName, readinessExchange, string.Empty).ConfigureAwait(false);
        var tenantId = Guid.NewGuid();
        var order = TenantPurchaseOrder.Create(tenantId, $"stripe-webhook-{tenantId:N}", TenantPlan.Business,
            "2026-10", TenantPlan.Business.DatabaseStrategy, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "owner", "owner");
        order.ReserveStripeIntentAttempt(DateTimeOffset.UtcNow);
        var created = await StripePurchases.CreateAsync(order, CancellationToken.None).ConfigureAwait(false);
        order.BindStripePaymentIntent(created.PaymentIntentId);
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.Add(order);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        var body = $"{{\"id\":\"evt_{Guid.NewGuid():N}\",\"object\":\"event\",\"type\":\"payment_intent.succeeded\",\"data\":{{\"object\":{{\"id\":\"{created.PaymentIntentId}\",\"object\":\"payment_intent\"}}}}}}";
        using (var forged = new HttpRequestMessage(HttpMethod.Post, "/stripe/webhooks") { Content = new StringContent(body, Encoding.UTF8, "application/json") })
        {
            forged.Headers.Add("Stripe-Signature", "t=1,v1=forged");
            using var denied = await Client.SendAsync(forged).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            Assert.Equal("pending", (await db.TenantPurchaseOrders.SingleAsync(item => item.Id == order.Id).ConfigureAwait(false)).Status);
        }

        using (var premature = CreateSignedWebhook(body))
        using (var refused = await Client.SendAsync(premature).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        }

        Assert.Null(await channel.BasicGetAsync(queue.QueueName, autoAck: true).ConfigureAwait(false));

        StripePurchases.MarkCaptured(created.PaymentIntentId);
        using (var first = CreateSignedWebhook(body))
        using (var confirmed = await Client.SendAsync(first).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var paid = await db.TenantPurchaseOrders.SingleAsync(item => item.Id == order.Id).ConfigureAwait(false);
            Assert.Equal("active", paid.Status);
            Assert.NotEqual(Guid.Empty, paid.SubscriptionId);
            Assert.Equal("captured", paid.PaymentMethod);
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        BasicGetResult? delivered = null;
        while (!deadline.IsCancellationRequested && delivered is null)
        {
            delivered = await channel.BasicGetAsync(queue.QueueName, autoAck: true).ConfigureAwait(false);
            if (delivered is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
        }

        Assert.NotNull(delivered);
        using var licenseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        IssuedLicense? issued = null;
        while (!licenseDeadline.IsCancellationRequested && issued is null)
        {
            await using var licenseScope = licensingFactory.Services.CreateAsyncScope();
            var db = licenseScope.ServiceProvider.GetRequiredService<LicensingDbContext>();
            issued = (await db.Licenses.AsNoTracking().SingleOrDefaultAsync(record =>
                record.SourceOrderId == order.Id).ConfigureAwait(false))?.ToIssuedLicense();
            if (issued is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
        }

        Assert.NotNull(issued);
        Assert.Equal(tenantId, issued.TenantId);
        Assert.Equal(order.Id, issued.SourceOrderId);
        await using (var licenseScope = licensingFactory.Services.CreateAsyncScope())
        {
            var issuer = licenseScope.ServiceProvider.GetRequiredService<LicenseIssuer>();
            var verified = await issuer.ValidateAsync(issued.LicenseDocument, tenantId,
                DateTimeOffset.UtcNow, [signingPair.ToPublicKeyString()], CancellationToken.None).ConfigureAwait(false);
            Assert.True(verified.IsValid, verified.Error);
            Assert.Equal(TenantPlan.Business.MaxAccessPointsPerLocation?.ToString() ?? "unlimited",
                verified.Features![LicenseFeatureKey.AccessPointsMax]);
        }
        using (var verifiedResponse = await licensingClient.PostAsJsonAsync("/licenses/verify",
            new { TenantId = tenantId, LicenseDocument = issued.LicenseDocument }).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, verifiedResponse.StatusCode);
            using var verificationJson = JsonDocument.Parse(await verifiedResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
            Assert.True(verificationJson.RootElement.GetProperty("isValid").GetBoolean());
            Assert.Equal(issued.LicenseId.ToString("D"), verificationJson.RootElement.GetProperty("licenseId").GetString());
            Assert.Equal(TenantPlan.Business.MaxAccessPointsPerLocation?.ToString() ?? "unlimited",
                verificationJson.RootElement.GetProperty("features").GetProperty(LicenseFeatureKey.AccessPointsMax).GetString());
        }

        var tamperedLicense = issued.LicenseDocument.Replace("2026-10", "2026-11", StringComparison.Ordinal);
        Assert.NotEqual(issued.LicenseDocument, tamperedLicense);
        using (var deniedResponse = await licensingClient.PostAsJsonAsync("/licenses/verify",
            new { TenantId = tenantId, LicenseDocument = tamperedLicense }).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, deniedResponse.StatusCode);
            using var denialJson = JsonDocument.Parse(await deniedResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
            Assert.False(denialJson.RootElement.GetProperty("isValid").GetBoolean());
        }

        using var readinessDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        BasicGetResult? readinessDelivery = null;
        while (!readinessDeadline.IsCancellationRequested && readinessDelivery is null)
        {
            readinessDelivery = await channel.BasicGetAsync(readinessQueue.QueueName, autoAck: true).ConfigureAwait(false);
            if (readinessDelivery is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
        }

        Assert.NotNull(readinessDelivery);
        var readiness = MemoryPackSerializer.Deserialize<TenantProvisioningReadyIntegrationEvent>(readinessDelivery.Body.Span);
        Assert.NotNull(readiness);
        Assert.Equal(issued.LicenseId, readiness.EvidenceId);
        Assert.Equal(order.Id, readiness.SourceOrderId);
        Assert.Equal(issued.SubscriptionId, readiness.SubscriptionId);
        Assert.True(TenantReadinessProof.Verify(readiness, Convert.FromBase64String(proofKey)));

        using var replay = CreateSignedWebhook(body);
        using var repeated = await Client.SendAsync(replay).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Null(await channel.BasicGetAsync(queue.QueueName, autoAck: true).ConfigureAwait(false));

        await using (var licenseScope = licensingFactory.Services.CreateAsyncScope())
        {
            await licenseScope.ServiceProvider.GetRequiredService<LicenseIssuer>()
                .RevokeAsync(issued.LicenseId, CancellationToken.None).ConfigureAwait(false);
        }

        using (var revokedResponse = await licensingClient.PostAsJsonAsync("/licenses/verify",
            new { TenantId = tenantId, LicenseDocument = issued.LicenseDocument }).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, revokedResponse.StatusCode);
            using var revokedJson = JsonDocument.Parse(await revokedResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
            Assert.False(revokedJson.RootElement.GetProperty("isValid").GetBoolean());
            Assert.Equal(JsonValueKind.Null, revokedJson.RootElement.GetProperty("features").ValueKind);
        }
    }

    private static HttpRequestMessage CreateSignedWebhook(string body)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var secret = Encoding.UTF8.GetBytes("whsec_local-only-billing-integration-tests");
        var digest = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/stripe/webhooks")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={Convert.ToHexStringLower(digest)}");
        return request;
    }
}
