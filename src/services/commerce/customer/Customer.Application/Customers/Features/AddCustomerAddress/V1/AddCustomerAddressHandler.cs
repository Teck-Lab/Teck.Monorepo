using Customers.Application.Customers.Mapping;
using Customers.Application.Customers.ReadModels;
using Customers.Application.Customers.Responses;
using Customers.Domain.Entities;
using ErrorOr;
using SharedKernel.Core.Database;

namespace Customers.Application.Customers.Features.AddCustomerAddress.V1;

/// <summary>Handles <see cref="AddCustomerAddressCommand"/>.</summary>
public static class AddCustomerAddressHandler
{
    /// <summary>Adds the address to the customer and commits the change.</summary>
    /// <param name="command">The command describing the customer and the new address.</param>
    /// <param name="repository">The write repository for loading and tracking the customer.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The newly added address DTO, or a NotFound error.</returns>
    public static async Task<ErrorOr<AddressDto>> Handle(
        AddCustomerAddressCommand command,
        IGenericWriteRepository<Customer, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var customer = await repository
            .FirstOrDefaultAsync(new CustomerByIdSpec(command.CustomerId), enableTracking: true, ct)
            .ConfigureAwait(false);

        if (customer is null)
        {
            return Error.NotFound(description: $"Customer '{command.CustomerId}' was not found.");
        }

        var addressId = customer.AddAddress(command.Line1, command.Line2, command.City, command.PostalCode, command.Country);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        var address = customer.Addresses.Single(a => a.Id == addressId);
        return address.ToDto();
    }
}
