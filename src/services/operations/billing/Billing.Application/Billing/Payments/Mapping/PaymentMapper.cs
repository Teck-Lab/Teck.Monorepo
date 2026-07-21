using Billings.Application.Billing.Payments.Responses;
using Billings.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Billings.Application.Billing.Payments.Mapping;

/// <summary>Compile-time mapping for payments.</summary>
// RequiredMappingStrategy.Target suppresses Mapperly RMG020 for the intentionally-dropped
// BaseEntity source fields (TenantId/CreatedAt/DomainEvents/...) while keeping RMG012 (unmapped
// target) active. Scope it here on the mapper — never via the repo-root .editorconfig.
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class PaymentMapper
{
    /// <summary>Maps a payment to a DTO.</summary>
    [MapProperty("Amount.Amount", nameof(PaymentDto.Amount))]
    [MapProperty("Amount.Currency", nameof(PaymentDto.Currency))]
    [MapProperty("Status.Name", nameof(PaymentDto.Status))]
    public static partial PaymentDto ToDto(this Payment payment);

    /// <summary>Maps payments to DTOs.</summary>
    public static partial IReadOnlyList<PaymentDto> ToDtos(this IEnumerable<Payment> payments);
}
