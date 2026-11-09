using System.Globalization;
using MemoryPack;
using SharedKernel.Events;
using Xunit;

namespace SharedKernel.UnitTests.Events;

public sealed class SubscriptionPaymentConfirmedEventTests
{
    [Fact]
    public void RoundTrip_PreservesConfirmedSubscriptionSnapshot()
    {
        var message = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SubscriptionId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            SourceOrderId = Guid.NewGuid(),
            PlanId = "business",
            TenantIdentifier = "acme",
            Features = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["access_points_max"] = "200",
                ["devices_max"] = "2000",
                ["products_max"] = "5000",
                ["max_locations"] = "25",
                ["supports_custom_branding"] = "True",
                ["supports_analytics"] = "True",
            },
            MaxAccessPointsPerLocation = 200,
            MaxDevicesPerLocation = 2000,
            MaxProductsPerLocation = 5000,
            MaxLocations = 25,
            SupportsCustomBranding = true,
            SupportsAnalytics = true,
            Currency = "USD",
            DatabaseStrategy = "dedicated",
            DatabaseProvider = "postgres",
            HasReadReplicas = true,
            ReplicaCount = 2,
            PaymentMethod = "external_invoice",
            ConfirmedAtUtc = DateTimeOffset.Parse("2026-10-25T10:00:00Z", CultureInfo.InvariantCulture),
            ValidFromUtc = DateTimeOffset.Parse("2026-10-25T10:00:00Z", CultureInfo.InvariantCulture),
            ValidUntilUtc = DateTimeOffset.Parse("2027-10-25T10:00:00Z", CultureInfo.InvariantCulture),
        };

        var actual = MemoryPackSerializer.Deserialize<SubscriptionPaymentConfirmedIntegrationEvent>(MemoryPackSerializer.Serialize(message));

        Assert.NotNull(actual);
        Assert.Equal(message.SubscriptionId, actual.SubscriptionId);
        Assert.Equal(message.TenantId, actual.TenantId);
        Assert.Equal(message.TenantIdentifier, actual.TenantIdentifier);
        Assert.Equal(message.Features, actual.Features);
        Assert.Equal(message.MaxAccessPointsPerLocation, actual.MaxAccessPointsPerLocation);
        Assert.Equal(message.MaxDevicesPerLocation, actual.MaxDevicesPerLocation);
        Assert.Equal(message.MaxProductsPerLocation, actual.MaxProductsPerLocation);
        Assert.Equal(message.MaxLocations, actual.MaxLocations);
        Assert.Equal(message.SupportsCustomBranding, actual.SupportsCustomBranding);
        Assert.Equal(message.SupportsAnalytics, actual.SupportsAnalytics);
        Assert.Equal(message.PlanVersion, actual.PlanVersion);
        Assert.Equal(message.AmountMinor, actual.AmountMinor);
        Assert.Equal(message.Currency, actual.Currency);
        Assert.Equal(message.DatabaseStrategy, actual.DatabaseStrategy);
        Assert.Equal(message.DatabaseProvider, actual.DatabaseProvider);
        Assert.Equal(message.HasReadReplicas, actual.HasReadReplicas);
        Assert.Equal(message.ReplicaCount, actual.ReplicaCount);
        Assert.Equal(message.PaymentMethod, actual.PaymentMethod);
        Assert.Equal(message.ConfirmedAtUtc, actual.ConfirmedAtUtc);
        Assert.Equal(message.ValidFromUtc, actual.ValidFromUtc);
        Assert.Equal(message.ValidUntilUtc, actual.ValidUntilUtc);
    }
}
