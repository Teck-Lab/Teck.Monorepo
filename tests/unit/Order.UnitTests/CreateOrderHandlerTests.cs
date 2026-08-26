using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Orders.Application.Database;
using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;
using Wolverine;
using Xunit;

namespace Orders.UnitTests;

public sealed class CreateOrderHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_ReturnsOrderDto()
    {
        var command = new CreateOrderCommand(
            CustomerId: Guid.NewGuid(),
            KeycloakSubjectId: "test-subject",
            BasketId: Guid.NewGuid(),
            TenantId: "tenant-1",
            AuthorizedAmount: 25m,
            Currency: "USD",
            PaymentMethodToken: "test-token",
            SourceCorrelationId: Guid.NewGuid().ToString("N"),
            Lines: [new CreateOrderLine(Guid.NewGuid(), "Test Product", 2, 12.50m)]);

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase($"order-unit-tests-{Guid.NewGuid()}")
            .Options;

        var tenantAccessor = TenantAccessor("tenant-1");
        var db = new OrderDbContext(options, tenantAccessor);
        var repository = new GenericWriteRepository<Order, Guid, OrderDbContext>(db, Substitute.For<IHttpContextAccessor>());
        var unitOfWork = new UnitOfWork<OrderDbContext>(db);

        var bus = Substitute.For<IMessageBus>();

        OrderDto result = await CreateOrderHandler.Handle(command, repository, unitOfWork, bus, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(command.CustomerId, result.CustomerId);
        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task Handle_DuplicateCheckout_DoesNotMatchAnotherOrdersRetryRequest()
    {
        const string tenantId = "tenant-a";
        const string originalCheckoutId = "checkout-original";
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase($"order-unit-tests-{Guid.NewGuid()}")
            .Options;
        var tenantAccessor = TenantAccessor(tenantId);
        var db = new OrderDbContext(options, tenantAccessor);
        var repository = new GenericWriteRepository<Order, Guid, OrderDbContext>(db, Substitute.For<IHttpContextAccessor>());
        var unitOfWork = new UnitOfWork<OrderDbContext>(db);
        var bus = Substitute.For<IMessageBus>();
        var collidingRetryOrder = Order.Create(
            Guid.NewGuid(),
            "subject-owner",
            Guid.NewGuid(),
            tenantId,
            [new OrderLine(Guid.NewGuid(), "Retry Order", 1, 10m)],
            10m,
            "USD",
            "checkout-other");
        Assert.IsType<Orders.Domain.DomainEvents.OrderPaymentActionRequired>(collidingRetryOrder.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed", "checkout-other"));
        Assert.True(collidingRetryOrder.BeginRetry(originalCheckoutId));
        var originalOrder = Order.Create(
            Guid.NewGuid(),
            "subject-owner",
            Guid.NewGuid(),
            tenantId,
            [new OrderLine(Guid.NewGuid(), "Original Order", 1, 10m)],
            10m,
            "USD",
            originalCheckoutId);
        await repository.AddAsync(collidingRetryOrder, CancellationToken.None);
        await repository.AddAsync(originalOrder, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        var duplicate = new CreateOrderCommand(
            originalOrder.CustomerId,
            originalOrder.KeycloakSubjectId,
            originalOrder.BasketId,
            tenantId,
            originalOrder.AuthorizedAmount,
            originalOrder.Currency,
            "token-replacement",
            originalCheckoutId,
            [new CreateOrderLine(originalOrder.Lines[0].ProductId, originalOrder.Lines[0].ProductName, originalOrder.Lines[0].Quantity, originalOrder.Lines[0].UnitPrice)]);

        var result = await CreateOrderHandler.Handle(duplicate, repository, unitOfWork, bus, CancellationToken.None);

        Assert.Equal(originalOrder.Id, result.Id);
        Assert.Equal(2, await db.Orders.CountAsync());
        await bus.DidNotReceive().PublishAsync(Arg.Any<OrderPlacedV2IntegrationEvent>());
    }

    private static IMultiTenantContextAccessor<TenantDetails> TenantAccessor(string tenantId)
    {
        var accessor = Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();
        accessor.MultiTenantContext.Returns(new MultiTenantContext<TenantDetails>(new TenantDetails
        {
            Id = tenantId,
            Identifier = tenantId,
            Name = tenantId,
            IsActive = true,
        }));
        return accessor;
    }
}
