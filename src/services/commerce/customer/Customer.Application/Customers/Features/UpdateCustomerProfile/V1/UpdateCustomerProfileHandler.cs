using Customers.Application.Customers.Mapping;
using Customers.Application.Customers.ReadModels;
using Customers.Application.Customers.Responses;
using Customers.Domain.Entities;
using ErrorOr;
using SharedKernel.Core.Database;

namespace Customers.Application.Customers.Features.UpdateCustomerProfile.V1;

/// <summary>Handles <see cref="UpdateCustomerProfileCommand"/>.</summary>
public static class UpdateCustomerProfileHandler
{
    /// <summary>Updates the customer's name and commits the change.</summary>
    /// <param name="command">The command describing the customer and new name.</param>
    /// <param name="repository">The write repository for loading and tracking the customer.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated customer DTO, or a NotFound error.</returns>
    public static async Task<ErrorOr<CustomerDto>> Handle(
        UpdateCustomerProfileCommand command,
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

        customer.UpdateProfile(command.FirstName, command.LastName);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return customer.ToDto();
    }
}
