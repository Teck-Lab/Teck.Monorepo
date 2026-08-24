using Billings.Domain.ValueObjects;

namespace Billings.Application.Billing.Payments.Features.ProcessPaymentOutcome.V1;

/// <summary>Contains a normalized provider attempt outcome.</summary>
/// <param name="AttemptStatus">The aggregate attempt status.</param>
/// <param name="DeclineCategory">The shopper-safe decline category.</param>
/// <param name="MappingAuditHash">The safe mapping audit hash.</param>
public sealed record PaymentOutcome(PaymentAttemptStatus AttemptStatus, DeclineCategory? DeclineCategory, string? MappingAuditHash);
