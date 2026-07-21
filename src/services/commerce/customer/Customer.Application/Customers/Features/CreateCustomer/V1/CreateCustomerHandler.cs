using Customers.Application.Customers.Mapping;
using Customers.Application.Customers.Responses;
using Customers.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Customers.Application.Customers.Features.CreateCustomer.V1;

/// <summary>Handles <see cref="CreateCustomerCommand"/>.</summary>
public static class CreateCustomerHandler
{
    /// <summary>
    /// Creates and persists a customer linked to the caller's Keycloak identity, then publishes
    /// <see cref="CustomerCreatedIntegrationEvent"/> once the commit succeeds.
    /// </summary>
    /// <param name="command">The command describing the customer to create.</param>
    /// <param name="repository">The write repository for persisting the customer.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="identity">The caller's Keycloak identity.</param>
    /// <param name="bus">The message bus used to publish the integration event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The newly created customer.</returns>
    public static async Task<CustomerDto> Handle(
        CreateCustomerCommand command,
        IGenericWriteRepository<Customer, Guid> repository,
        IUnitOfWork unitOfWork,
        ITenantInfo tenant,
        ICustomerIdentityAccessor identity,
        IMessageBus bus,
        CancellationToken ct)
    {
        var subject = identity.KeycloakSubjectId ?? string.Empty;
        var customer = Customer.Create(tenant.Id ?? string.Empty, subject, command.Email, command.FirstName, command.LastName);

        await repository.AddAsync(customer, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new CustomerCreatedIntegrationEvent
        {
            CustomerId = customer.Id,
            TenantId = customer.TenantId,
            KeycloakSubjectId = subject,
            Email = customer.Email,
        }).ConfigureAwait(false);

        return customer.ToDto();
    }
}
