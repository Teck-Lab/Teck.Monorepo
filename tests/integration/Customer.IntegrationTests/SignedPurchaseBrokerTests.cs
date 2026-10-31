using System.Security.Cryptography;
using Customers.Application.Database;
using Customers.Application.EventHandlers.IntegrationEvents;
using Customers.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Wolverine.RabbitMQ;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>Exercises the real Customer RabbitMQ listener with an authenticated paid snapshot.</summary>
[Collection("SharedTestcontainers")]
public sealed class SignedPurchaseBrokerTests(SharedTestcontainersFixture fixture)
    : CustomerIntegrationTestBase(fixture, useRabbitMq: true,
        organizationProvisioner: new TestOrganizationProvisioner(), billingPublicKey: Keys.Value.Public)
{
    private static readonly Lazy<(string Private, string Public)> Keys = new(CreateKeys);

    [Fact]
    public async Task Signed_paid_message_reaches_Customer_but_does_not_grant_database_or_license_readiness()
    {
        _ = Client;
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), $"broker-reservation-{Guid.NewGuid():N}", "owner-subject");
        await SeedTenantAsync(tenant).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            TenantId = tenant.Id,
            TenantIdentifier = tenant.Identifier,
            SourceOrderId = Guid.NewGuid(),
            SubscriptionId = Guid.NewGuid(),
            PlanId = "Business",
            PlanVersion = "2026-10",
            PaymentMethod = "captured",
            AmountMinor = 10000,
            Currency = "USD",
            DatabaseStrategy = "shared",
            DatabaseProvider = "postgres",
            MaxLocations = 2,
            Features = new Dictionary<string, string> { ["access-points-max"] = "25" },
            ConfirmedAtUtc = now,
            ValidFromUtc = now,
            ValidUntilUtc = now.AddDays(30),
        };
        using (var privateKey = RSA.Create())
        {
            privateKey.ImportFromPem(Keys.Value.Private);
            paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);
        }

        using var publisher = await global::Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseWolverine(options => options.UseRabbitMq(new Uri(fixture.RabbitMqConnectionString, UriKind.Absolute))
                .AutoProvision()
                .UseConventionalRouting())
            .StartAsync().ConfigureAwait(false);
        using (var senderScope = publisher.Services.CreateScope())
        {
            await senderScope.ServiceProvider.GetRequiredService<IMessageBus>()
                .PublishAsync(paid, new DeliveryOptions { TenantId = tenant.Id.ToString("D") })
                .ConfigureAwait(false);
        }

        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var scope = CreateServiceScope();
            var stored = await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Tenants
                .AsNoTracking().SingleAsync(item => item.Id == tenant.Id).ConfigureAwait(false);
            if (stored.KeycloakOrganizationId.HasValue)
            {
                Assert.Equal("paid_pending_readiness", stored.Status);
                Assert.Equal(paid.SourceOrderId, stored.PurchaseOrderId);
                Assert.Equal(paid.SubscriptionId, stored.SubscriptionId);
                Assert.False(stored.LicenseReady);
                Assert.False(stored.DatabaseReady);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
        }

        throw new Xunit.Sdk.XunitException("The Customer RabbitMQ listener did not apply the signed paid reservation.");
    }

    private static (string Private, string Public) CreateKeys()
    {
        using var key = RSA.Create(2048);
        return (key.ExportRSAPrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    private sealed class TestOrganizationProvisioner : IOrganizationProvisioner
    {
        private readonly Guid organizationId = Guid.NewGuid();

        public Task<Guid> EnsureOwnerOrganizationAsync(Guid tenantId, string identifier, string ownerSubjectId,
            CancellationToken cancellationToken) => Task.FromResult(organizationId);
    }
}
