using MemoryPack;
using SharedKernel.Core.CQRS;

namespace Billings.Application.Billing.Payments.Features.ProcessPaymentOutcome.V1;

/// <summary>Applies an immediate or delayed provider outcome exactly once.</summary>
/// <param name="OrderId">The order identifier.</param>
/// <param name="RequestId">The stable provider request identifier.</param>
/// <param name="Outcome">The normalized provider outcome.</param>
/// <param name="ProviderReference">The tokenized provider reference, if any.</param>
/// <param name="ProviderCode">The billing-private provider code, if any.</param>
/// <param name="IsLegacy">Whether the retained V1 outcome event contract must be published.</param>
[MemoryPackable]
public sealed partial record ProcessPaymentOutcomeCommand(Guid OrderId, string RequestId, string Outcome, string? ProviderReference, string? ProviderCode, bool IsLegacy = false) : ICommand;
