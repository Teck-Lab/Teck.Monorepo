namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to create a supplier.</summary>
/// <param name="Name">The supplier name.</param>
/// <param name="ContactEmail">The optional contact email.</param>
/// <param name="ContactPhone">The optional contact phone.</param>
public sealed record CreateSupplierRequest(string Name, string? ContactEmail, string? ContactPhone);
