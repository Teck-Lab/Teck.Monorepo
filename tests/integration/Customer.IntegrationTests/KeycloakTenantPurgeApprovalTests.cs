using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Customers.Application.Database;
using Customers.Domain.Entities;
using JasperFx.CommandLine;
using MemoryPack;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using RabbitMQ.Client;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>Exercises two distinct Keycloak operators approving one retained tenant in Customer's global registry.</summary>
[Collection("KeycloakTestRealm")]
public sealed class KeycloakTenantPurgeApprovalTests(KeycloakTestRealmFixture keycloak, SharedTestcontainersFixture containers) : IAsyncLifetime
{
    private const string TokenClientId = "integration-test-client";
    private const string TokenSecret = "local-only-test-integration-token-client-secret-not-for-production";
    private const string CustomerSecret = "local-only-test-customer-api-secret-not-for-production";
    private static readonly Lazy<(string PrivateKey, string PublicKey)> ApprovalKeys = new(() =>
    {
        using RSA key = RSA.Create(2048);
        return (key.ExportEncryptedPkcs8PrivateKeyPem("local-only-test-purge-passphrase",
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 10000)),
            key.ExportSubjectPublicKeyInfoPem());
    });
    private CustomerWebApplicationFactory? factory;
    private HttpClient? client;

    public async ValueTask InitializeAsync()
    {
        var connection = await containers.CreateSharedTestDatabaseAsync(typeof(CustomerDbContext), "Customer.Host");
        factory = new CustomerWebApplicationFactory(connection, keycloak.Authority, ApprovalKeys.Value.PrivateKey,
            containers.RabbitMqConnectionString);
        client = factory.CreateClient();
    }

    [Fact]
    public async Task ApprovePurge_RequiresTwoDifferentManagersRetainedPaidPeriodAndPreservesAudit()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var suspended = DateTimeOffset.UtcNow.AddDays(-32);
        await using (var scope = factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var tenant = Tenant.CreatePendingReservation(tenantId, $"purge-{tenantId:N}", "buyer-subject");
            tenant.ApplyPurchaseConfirmation(orderId, subscriptionId, "Business", "2026-10", "dedicated", "postgres", false,
                DateTimeOffset.UtcNow.AddYears(1));
            tenant.SuspendSubscription(orderId, subscriptionId, suspended);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        var rabbitFactory = new ConnectionFactory { Uri = new Uri(containers.RabbitMqConnectionString, UriKind.Absolute) };
        await using var rabbit = await rabbitFactory.CreateConnectionAsync();
        await using var channel = await rabbit.CreateChannelAsync();
        const string exchange = "SharedKernel.Events.TenantPurgeApprovedIntegrationEvent";
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true);
        var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue.QueueName, exchange, string.Empty);
        var firstToken = await keycloak.GetUserTokenAsync("integration-manager", "local-only-test-manager-password",
            TokenClientId, clientSecret: TokenSecret, requestedScope: "customer-api-audience");
        var secondToken = await keycloak.GetUserTokenAsync("integration-foreign-manager", "local-only-test-foreign-manager-password",
            TokenClientId, clientSecret: TokenSecret, requestedScope: "customer-api-audience");
        var readerToken = await keycloak.GetUserTokenAsync("integration-reader", "local-only-test-reader-password",
            TokenClientId, clientSecret: TokenSecret, requestedScope: "customer-api-audience");
        string path = $"/tenants/{tenantId:D}/purge-approvals";

        using var denied = await ApproveAsync(path, readerToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var first = await ApproveAsync(path, firstToken);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        using var repeat = await ApproveAsync(path, firstToken);
        Assert.Null(await channel.BasicGetAsync(queue.QueueName, autoAck: true));
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        using var second = await ApproveAsync(path, secondToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var approved = await second.Content.ReadFromJsonAsync<TenantPurgeApprovedIntegrationEvent>();
        Assert.NotNull(approved);
        Assert.Equal(tenantId, approved.TenantId);
        Assert.Equal(orderId, approved.SourceOrderId);
        using RSA verification = RSA.Create();
        verification.ImportFromPem(ApprovalKeys.Value.PublicKey);
        Assert.True(TenantPurgeApprovalProof.Verify(approved, verification));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        BasicGetResult? delivery = null;
        while (!deadline.IsCancellationRequested && delivery is null)
        {
            delivery = await channel.BasicGetAsync(queue.QueueName, autoAck: true);
            if (delivery is null) await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        Assert.NotNull(delivery);
        var brokerDecision = MemoryPackSerializer.Deserialize<TenantPurgeApprovedIntegrationEvent>(delivery.Body.Span);
        Assert.NotNull(brokerDecision);
        Assert.Equal(approved.ApprovalId, brokerDecision.ApprovalId);
        Assert.True(TenantPurgeApprovalProof.Verify(brokerDecision, verification));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            Assert.Equal(2, await db.TenantPurgeApprovals.CountAsync(entry => entry.TenantId == tenantId && entry.SourceOrderId == orderId));
        }
    }

    [Fact]
    public async Task ApprovePurge_RejectsWrongTenantAndUnexpiredRetention()
    {
        var manager = await keycloak.GetUserTokenAsync("integration-manager", "local-only-test-manager-password",
            TokenClientId, clientSecret: TokenSecret, requestedScope: "customer-api-audience");
        using var missing = await ApproveAsync($"/tenants/{Guid.NewGuid():D}/purge-approvals", manager);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var tenantId = Guid.NewGuid();
        await using (var scope = factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var tenant = Tenant.CreatePendingReservation(tenantId, $"unexpired-{tenantId:N}", "buyer-subject");
            var orderId = Guid.NewGuid();
            var subscriptionId = Guid.NewGuid();
            tenant.ApplyPurchaseConfirmation(orderId, subscriptionId, "Business", "2026-10", "dedicated", "postgres", false,
                DateTimeOffset.UtcNow.AddYears(1));
            tenant.SuspendSubscription(orderId, subscriptionId, DateTimeOffset.UtcNow.AddDays(-29));
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }
        using var premature = await ApproveAsync($"/tenants/{tenantId:D}/purge-approvals", manager);
        Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            Assert.False(await db.TenantPurgeApprovals.AnyAsync(entry => entry.TenantId == tenantId));
        }
    }

    private async Task<HttpResponseMessage> ApproveAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client!.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
    }

    private sealed class CustomerWebApplicationFactory(string connection, string authority, string privateKey,
        string rabbitMqConnectionString) : WebApplicationFactory<Program>
    {
        static CustomerWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CustomerWrite", connection);
            builder.UseSetting("ConnectionStrings:CustomerRead", connection);
            builder.UseSetting("ConnectionStrings:Default", connection);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitMqConnectionString);
            builder.UseSetting("Keycloak:realm", KeycloakTestRealmFixture.Realm);
            builder.UseSetting("Keycloak:auth-server-url", authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)]);
            builder.UseSetting("Keycloak:resource", "customer-api");
            builder.UseSetting("Keycloak:credentials:secret", CustomerSecret);
            builder.UseSetting("PurgeApproval:EncryptedPrivateKey", privateKey);
            builder.UseSetting("PurgeApproval:Passphrase", "local-only-test-purge-passphrase");
            builder.UseSetting("PurgeApproval:RecoveryDays", "30");
            builder.ConfigureTestServices(services => services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.MetadataAddress = authority + "/.well-known/openid-configuration";
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        options.MetadataAddress,
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever { RequireHttps = false });
                }));
        }
    }
}
