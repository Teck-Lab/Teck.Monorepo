// <copyright file="CustomerContactReconciliationTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Customers.Application.Database;
using Customers.Application.Customers.EventHandlers.IntegrationEvents;
using Customers.Domain.Entities;
using Customers.Host.Database;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>Integration coverage for resolving pre-existing customer contacts asynchronously.</summary>
[Collection("SharedTestcontainers")]
public sealed class CustomerContactReconciliationTests : CustomerIntegrationTestBase
{
    private readonly SharedTestcontainersFixture fixture;

    /// <summary>Initializes the test against the shared PostgreSQL fixture.</summary>
    /// <param name="fixture">The shared Testcontainers fixture.</param>
    public CustomerContactReconciliationTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Publishes the current persisted email through the shared asynchronous response event.</summary>
    [Fact]
    public async Task Handle_PreExistingCustomer_PublishesReconciledContactWithoutSynchronousServiceCall()
    {
        const string tenantId = "tenant-reconciliation";
        const string subjectId = "subject-reconciliation";
        var customer = Customer.Create(tenantId, subjectId, "persisted-contact@example.test", "Ada", "Lovelace");
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(CustomerDbContext), "Customer.Host");

        await using (var writeContext = CreateWriteContext(connectionString, tenantId))
        {
            writeContext.Set<Customer>().Add(customer);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateReadContext(connectionString, tenantId);
        var customers = new CustomerReadRepository<Customer, Guid>(readContext);
        var bus = Substitute.For<IMessageBus>();
        CustomerContactReconciledIntegrationEvent? published = null;
        bus.PublishAsync(Arg.Do<CustomerContactReconciledIntegrationEvent>(evt => published = evt))
            .Returns(ValueTask.CompletedTask);
        var request = new CustomerContactReconciliationRequestedIntegrationEvent
        {
            CustomerId = customer.Id,
            KeycloakSubjectId = subjectId,
            TenantId = tenantId,
            RequestId = $"contact:{tenantId}:{customer.Id}:{subjectId}",
            SourceCorrelationId = "order:pre-existing-customer",
        };

        await CustomerContactReconciliationRequestedHandler.Handle(request, customers, bus, CancellationToken.None);

        var response = Assert.IsType<CustomerContactReconciledIntegrationEvent>(published);
        Assert.Equal(customer.Id, response.CustomerId);
        Assert.Equal(subjectId, response.KeycloakSubjectId);
        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal(customer.Email, response.Email);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(request.SourceCorrelationId, response.SourceCorrelationId);
        await bus.Received(1).PublishAsync(Arg.Any<CustomerContactReconciledIntegrationEvent>());
        Assert.DoesNotContain(
            typeof(CustomerContactReconciliationRequestedHandler).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name?.Contains("Notification", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>Resolves a persisted contact when the event has only the immutable subject.</summary>
    [Fact]
    public async Task Handle_SubjectOnlyRequest_PublishesReconciledContact()
    {
        const string tenantId = "tenant-subject-only";
        const string subjectId = "subject-only";
        var customer = Customer.Create(tenantId, subjectId, "subject-only@example.test", "Ada", "Lovelace");
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(CustomerDbContext), "Customer.Host");

        await using (var writeContext = CreateWriteContext(connectionString, tenantId))
        {
            writeContext.Set<Customer>().Add(customer);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateReadContext(connectionString, tenantId);
        var customers = new CustomerReadRepository<Customer, Guid>(readContext);
        var bus = Substitute.For<IMessageBus>();
        CustomerContactReconciledIntegrationEvent? published = null;
        bus.PublishAsync(Arg.Do<CustomerContactReconciledIntegrationEvent>(evt => published = evt))
            .Returns(ValueTask.CompletedTask);
        var request = new CustomerContactReconciliationRequestedIntegrationEvent
        {
            CustomerId = Guid.Empty,
            KeycloakSubjectId = subjectId,
            TenantId = tenantId,
            RequestId = $"contact:{tenantId}::{subjectId}",
            SourceCorrelationId = "order:subject-only-customer",
        };

        await CustomerContactReconciliationRequestedHandler.Handle(request, customers, bus, CancellationToken.None);

        var response = Assert.IsType<CustomerContactReconciledIntegrationEvent>(published);
        Assert.Equal(customer.Id, response.CustomerId);
        Assert.Equal(subjectId, response.KeycloakSubjectId);
        Assert.Equal(tenantId, response.TenantId);
        Assert.Equal(customer.Email, response.Email);
        Assert.Equal(request.RequestId, response.RequestId);
        await bus.Received(1).PublishAsync(Arg.Any<CustomerContactReconciledIntegrationEvent>());
    }

    /// <summary>Rejects a request whose supplied customer id and immutable subject disagree.</summary>
    [Fact]
    public async Task Handle_NonEmptyCustomerIdWithMismatchedSubject_DoesNotPublish()
    {
        const string tenantId = "tenant-subject-mismatch";
        var customer = Customer.Create(tenantId, "subject-a", "mismatch@example.test", "Ada", "Lovelace");
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(CustomerDbContext), "Customer.Host");

        await using (var writeContext = CreateWriteContext(connectionString, tenantId))
        {
            writeContext.Set<Customer>().Add(customer);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateReadContext(connectionString, tenantId);
        var customers = new CustomerReadRepository<Customer, Guid>(readContext);
        var bus = Substitute.For<IMessageBus>();
        var request = new CustomerContactReconciliationRequestedIntegrationEvent
        {
            CustomerId = customer.Id,
            KeycloakSubjectId = "subject-b",
            TenantId = tenantId,
            RequestId = $"contact:{tenantId}:{customer.Id}:subject-b",
            SourceCorrelationId = "order:subject-mismatch",
        };

        await CustomerContactReconciliationRequestedHandler.Handle(request, customers, bus, CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(Arg.Any<CustomerContactReconciledIntegrationEvent>());
    }

    private static CustomerDbContext CreateWriteContext(string connectionString, string tenantId)
    {
        var options = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Customer.Host"))
            .UseTeckCloudTenant(tenantId)
            .Options;
        return new CustomerDbContext(options, null!);
    }

    private static CustomerReadDbContext CreateReadContext(string connectionString, string tenantId)
    {
        var options = new DbContextOptionsBuilder<CustomerReadDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(tenantId)
            .Options;
        return new CustomerReadDbContext(options, null!);
    }
}
