using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;
using SharedKernel.Core.Database;

namespace Catalog.Application.Suppliers.Features.CreateSupplier.V1;

/// <summary>Handles <see cref="CreateSupplierCommand"/>.</summary>
public static class CreateSupplierHandler
{
    /// <summary>Creates and persists a supplier. TenantId is stamped by the Host interceptor on save.</summary>
    /// <param name="command">The command describing the supplier to create.</param>
    /// <param name="repository">The write repository for persisting the supplier.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<SupplierDto> Handle(
        CreateSupplierCommand command,
        IGenericWriteRepository<Supplier, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var supplier = Supplier.Create(tenantId: null, command.Name, command.ContactEmail, command.ContactPhone);
        await repository.AddAsync(supplier, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return supplier.ToDto();
    }
}
