using Catalog.Application.Database;
using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;

namespace Catalog.Application.Suppliers.Features.CreateSupplier.V1;

/// <summary>Handles <see cref="CreateSupplierCommand"/>.</summary>
public static class CreateSupplierHandler
{
    /// <summary>Creates and persists a supplier. TenantId is stamped by the Host interceptor on save.</summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<SupplierDto> Handle(
        CreateSupplierCommand command,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var supplier = Supplier.Create(string.Empty, command.Name, command.ContactEmail, command.ContactPhone);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return supplier.ToDto();
    }
}
