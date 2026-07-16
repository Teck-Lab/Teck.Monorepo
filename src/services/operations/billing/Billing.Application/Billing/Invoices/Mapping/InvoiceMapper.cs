using Billings.Application.Billing.Invoices.Responses;
using Billings.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Billings.Application.Billing.Invoices.Mapping;

/// <summary>Compile-time mapping for invoices and their lines.</summary>
// RequiredMappingStrategy.Target suppresses Mapperly RMG020 for the intentionally-dropped
// BaseEntity source fields (TenantId/CreatedAt/DomainEvents/...) while keeping RMG012 (unmapped
// target) active. Scope it here on the mapper — never via the repo-root .editorconfig.
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class InvoiceMapper
{
    /// <summary>Maps an invoice (and its lines) to a DTO.</summary>
    [MapProperty("Amount.Amount", nameof(InvoiceDto.Amount))]
    [MapProperty("Amount.Currency", nameof(InvoiceDto.Currency))]
    public static partial InvoiceDto ToDto(this Invoice invoice);
}
