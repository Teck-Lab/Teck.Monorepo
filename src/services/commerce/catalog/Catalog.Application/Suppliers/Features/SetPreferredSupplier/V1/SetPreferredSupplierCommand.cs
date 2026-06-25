using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.SetPreferredSupplier.V1;

/// <summary>Sets the single preferred supplier for a variant.</summary>
public sealed record SetPreferredSupplierCommand(Guid VariantId, Guid SupplierId) : ICommand<ErrorOr<Success>>;
