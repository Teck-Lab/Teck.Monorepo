using Billings.Application.Billing.Payments.Responses;
using SharedKernel.Core.CQRS;

namespace Billings.Application.Billing.Payments.Features.ListPayments.V1;

/// <summary>Fetches all payments, most recently created first.</summary>
public sealed record ListPaymentsQuery : IQuery<IReadOnlyList<PaymentDto>>;
