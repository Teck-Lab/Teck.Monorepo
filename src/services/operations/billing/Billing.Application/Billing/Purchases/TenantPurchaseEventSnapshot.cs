using Billings.Domain.Entities;
using SharedKernel.Core.Pricing;
using SharedKernel.Events;

namespace Billings.Application.Billing.Purchases;

/// <summary>Projects a confirmed, server-priced purchase into its immutable consumer snapshot.</summary>
internal static class TenantPurchaseEventSnapshot
{
    internal static SubscriptionPaymentConfirmedIntegrationEvent From(TenantPurchaseOrder order, SubscriptionPaymentSigningKey signer)
    {
        if (!TenantPlan.TryFromName(order.PlanId, ignoreCase: true, out var plan))
        {
            throw new InvalidOperationException("The quoted plan is not present in the server plan catalogue.");
        }

        static string Limit(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unlimited";
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SubscriptionId = order.SubscriptionId,
            TenantId = order.TenantId,
            TenantIdentifier = order.TenantIdentifier,
            SourceOrderId = order.Id,
            PreviousSourceOrderId = order.PreviousPurchaseOrderId,
            PlanId = order.PlanId,
            PlanVersion = order.PlanVersion,
            AmountMinor = order.AmountMinor,
            Currency = order.Currency,
            DatabaseStrategy = order.DatabaseStrategy,
            DatabaseProvider = order.DatabaseProvider,
            HasReadReplicas = order.HasReadReplicas,
            ReplicaCount = order.ReplicaCount,
            PaymentMethod = order.PaymentMethod,
            ConfirmedAtUtc = order.ConfirmedAtUtc!.Value,
            ValidFromUtc = order.ValidFromUtc!.Value,
            ValidUntilUtc = order.ValidUntilUtc!.Value,
            MaxAccessPointsPerLocation = plan.MaxAccessPointsPerLocation,
            MaxDevicesPerLocation = plan.MaxDevicesPerLocation,
            MaxProductsPerLocation = plan.MaxProductsPerLocation,
            MaxLocations = plan.MaxLocations,
            SupportsCustomBranding = plan.SupportsCustomBranding,
            SupportsAnalytics = plan.SupportsAnalytics,
            Features = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [LicenseFeatureKey.AccessPointsMax] = Limit(plan.MaxAccessPointsPerLocation),
                [LicenseFeatureKey.DevicesMax] = Limit(plan.MaxDevicesPerLocation),
                [LicenseFeatureKey.ProductsMax] = Limit(plan.MaxProductsPerLocation),
                [LicenseFeatureKey.MaxLocations] = plan.MaxLocations.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [LicenseFeatureKey.SupportsCustomBranding] = plan.SupportsCustomBranding.ToString(),
                [LicenseFeatureKey.SupportsAnalytics] = plan.SupportsAnalytics.ToString(),
            },
        };
        paid.Proof = signer.Sign(paid);
        return paid;
    }
}
