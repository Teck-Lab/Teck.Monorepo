namespace Billings.Host.Endpoints.Purchases;

/// <summary>Audited invoice-verification payload.</summary>
public sealed class ConfirmExternalInvoiceRequest
{
    /// <summary>Gets or sets the invoice reference.</summary>
    public string InvoiceReference { get; set; } = string.Empty;
}
