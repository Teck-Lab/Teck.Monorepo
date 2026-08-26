using Billings.Domain.ValueObjects;

namespace Billings.Application.Billing.Payments;

/// <summary>Contains a safe decline mapping and its audit evidence.</summary>
/// <param name="Category">The shopper-safe category.</param>
/// <param name="AuditHash">The non-sensitive mapping audit hash.</param>
public sealed record DeclineResolution(DeclineCategory Category, string AuditHash);
