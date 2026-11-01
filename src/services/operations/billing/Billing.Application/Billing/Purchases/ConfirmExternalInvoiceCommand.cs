namespace Billings.Application.Billing.Purchases;

/// <summary>Privileged external invoice confirmation request.</summary>
/// <param name="OrderId">The pending purchase order identifier.</param>
/// <param name="InvoiceReference">The external invoice reference.</param>
/// <param name="VerifiedBy">The authenticated verifier subject.</param>
public sealed record ConfirmExternalInvoiceCommand(Guid OrderId, string InvoiceReference, string VerifiedBy);
