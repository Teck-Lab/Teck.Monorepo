using Customers.Application.Customers.ReadModels;
using Customers.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Customers.Application.Customers.EventHandlers.IntegrationEvents;

/// <summary>Returns a current tenant-scoped customer contact through the asynchronous event boundary.</summary>
public static class CustomerContactReconciliationRequestedHandler
{
    /// <summary>Publishes a reconciled contact only when the requested customer and subject agree.</summary>
    /// <param name="evt">The asynchronous contact reconciliation request.</param>
    /// <param name="customers">The tenant-scoped customer read repository.</param>
    /// <param name="bus">The shared message bus used to publish the response.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after publishing a matching contact response, if any.</returns>
    public static async Task Handle(CustomerContactReconciliationRequestedIntegrationEvent evt, IGenericReadRepository<Customer, Guid> customers, IMessageBus bus, CancellationToken ct)
    {
        var customer = evt.CustomerId == Guid.Empty
            ? await customers.FirstOrDefaultAsync(new CustomerBySubjectSpec(evt.KeycloakSubjectId), ct).ConfigureAwait(false)
            : await customers.FirstOrDefaultAsync(new CustomerByIdSpec(evt.CustomerId), ct).ConfigureAwait(false);
        if (customer is null || (evt.CustomerId != Guid.Empty && !string.Equals(customer.KeycloakSubjectId, evt.KeycloakSubjectId, StringComparison.Ordinal)))
        {
            return;
        }

        await bus.PublishAsync(new CustomerContactReconciledIntegrationEvent { CustomerId = customer.Id, KeycloakSubjectId = customer.KeycloakSubjectId, TenantId = customer.TenantId, Email = customer.Email, RequestId = evt.RequestId, SourceCorrelationId = evt.SourceCorrelationId }).ConfigureAwait(false);
    }
}
