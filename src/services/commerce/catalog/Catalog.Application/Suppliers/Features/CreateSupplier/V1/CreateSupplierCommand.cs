using Catalog.Application.Suppliers.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.CreateSupplier.V1;

/// <summary>Creates a supplier.</summary>
public sealed record CreateSupplierCommand(string Name, string? ContactEmail, string? ContactPhone) : ICommand<SupplierDto>;
