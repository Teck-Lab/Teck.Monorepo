namespace Catalog.Application.Suppliers.Responses;

/// <summary>A supplier.</summary>
public sealed record SupplierDto(Guid Id, string Name, string? ContactEmail, string? ContactPhone, bool IsActive);
