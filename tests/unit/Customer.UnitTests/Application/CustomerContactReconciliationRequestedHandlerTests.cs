using Ardalis.Specification;
using Customers.Application.Customers.EventHandlers.IntegrationEvents;
using Customers.Application.Customers.ReadModels;
using CustomerEntity = Customers.Domain.Entities.Customer;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Customer.UnitTests.Application;

public sealed class CustomerContactReconciliationRequestedHandlerTests
{
    [Fact]
    public async Task Handle_PublishesTenantScopedContactResponseForTheRequestedCustomer()
    {
        var customer = CustomerEntity.Create("tenant-a", "subject-a", "shopper@example.test", "Ada", "Lovelace");
        var customers = Substitute.For<IGenericReadRepository<CustomerEntity, Guid>>();
        var bus = Substitute.For<IMessageBus>();
        CustomerContactReconciledIntegrationEvent? response = null;
        customers.FirstOrDefaultAsync(Arg.Any<ISpecification<CustomerEntity>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CustomerEntity?>(customer));
        bus.PublishAsync(Arg.Do<CustomerContactReconciledIntegrationEvent>(value => response = value))
            .Returns(ValueTask.CompletedTask);
        var request = new CustomerContactReconciliationRequestedIntegrationEvent { CustomerId = customer.Id, KeycloakSubjectId = "subject-a", TenantId = "tenant-a", RequestId = "contact:tenant-a", SourceCorrelationId = "order:event" };

        await CustomerContactReconciliationRequestedHandler.Handle(request, customers, bus, CancellationToken.None);

        var published = Assert.IsType<CustomerContactReconciledIntegrationEvent>(response);
        Assert.Equal(customer.Id, published.CustomerId);
        Assert.Equal("tenant-a", published.TenantId);
        Assert.Equal("subject-a", published.KeycloakSubjectId);
        Assert.Equal("shopper@example.test", published.Email);
        Assert.Equal("contact:tenant-a", published.RequestId);
        Assert.Equal("order:event", published.SourceCorrelationId);
        await customers.Received(1).FirstOrDefaultAsync(Arg.Any<CustomerByIdSpec>(), CancellationToken.None);
    }

    [Fact]
    public async Task Handle_SubjectOnlyRequest_PublishesTenantScopedContactResponse()
    {
        var customer = CustomerEntity.Create("tenant-a", "subject-a", "shopper@example.test", "Ada", "Lovelace");
        var customers = Substitute.For<IGenericReadRepository<CustomerEntity, Guid>>();
        var bus = Substitute.For<IMessageBus>();
        CustomerContactReconciledIntegrationEvent? response = null;
        customers.FirstOrDefaultAsync(Arg.Any<ISpecification<CustomerEntity>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CustomerEntity?>(customer));
        bus.PublishAsync(Arg.Do<CustomerContactReconciledIntegrationEvent>(value => response = value))
            .Returns(ValueTask.CompletedTask);
        var request = new CustomerContactReconciliationRequestedIntegrationEvent { CustomerId = Guid.Empty, KeycloakSubjectId = customer.KeycloakSubjectId, TenantId = customer.TenantId, RequestId = "contact:tenant-a::subject-a", SourceCorrelationId = "order:subject-only" };

        await CustomerContactReconciliationRequestedHandler.Handle(request, customers, bus, CancellationToken.None);

        var published = Assert.IsType<CustomerContactReconciledIntegrationEvent>(response);
        Assert.Equal(customer.Id, published.CustomerId);
        Assert.Equal(customer.KeycloakSubjectId, published.KeycloakSubjectId);
        Assert.Equal(customer.Email, published.Email);
        await customers.Received(1).FirstOrDefaultAsync(Arg.Any<CustomerBySubjectSpec>(), CancellationToken.None);
        await customers.DidNotReceive().FirstOrDefaultAsync(Arg.Any<CustomerByIdSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NonEmptyCustomerIdWithMismatchedSubject_DoesNotPublish()
    {
        var customer = CustomerEntity.Create("tenant-a", "subject-a", "shopper@example.test", "Ada", "Lovelace");
        var customers = Substitute.For<IGenericReadRepository<CustomerEntity, Guid>>();
        var bus = Substitute.For<IMessageBus>();
        customers.FirstOrDefaultAsync(Arg.Any<ISpecification<CustomerEntity>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CustomerEntity?>(customer));
        var request = new CustomerContactReconciliationRequestedIntegrationEvent { CustomerId = customer.Id, KeycloakSubjectId = "subject-b", TenantId = customer.TenantId, RequestId = "contact:tenant-a:mismatch", SourceCorrelationId = "order:mismatch" };

        await CustomerContactReconciliationRequestedHandler.Handle(request, customers, bus, CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(Arg.Any<CustomerContactReconciledIntegrationEvent>());
        await customers.Received(1).FirstOrDefaultAsync(Arg.Any<CustomerByIdSpec>(), CancellationToken.None);
    }
}
