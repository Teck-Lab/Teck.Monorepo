using Customers.Application.Customers.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Customers.Application.Customers.Features.AddCustomerAddress.V1;

/// <summary>Adds a new address to a customer.</summary>
public sealed record AddCustomerAddressCommand(Guid CustomerId, string Line1, string? Line2, string City, string PostalCode, string Country)
    : ICommand<ErrorOr<AddressDto>>;
