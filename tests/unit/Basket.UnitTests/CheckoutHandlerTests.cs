using Ardalis.Specification;
using Baskets.Application.Baskets;
using Baskets.Application.Baskets.Features.Checkout.V1;
using Baskets.Domain.Entities;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Baskets.UnitTests;

public sealed class CheckoutHandlerTests
{
    [Fact]
    public async Task Handle_ChecksOutBasketCommitsAndPublishesIntegrationEvent()
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var basket = Basket.CreateForCustomer(customerId, "tenant-1");
        basket.AddItem(productId, "Widget", 10m, 2);
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.CustomerId.Returns(customerId);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        var dto = await CheckoutHandler.Handle(new CheckoutCommand(basket.Id), repository, identity, unitOfWork, bus, CancellationToken.None);

        Assert.Equal("CheckedOut", dto.Status);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // The checkout->order loop depends on this event actually being published; assert its shape,
        // including the per-line field mapping that must not swap Quantity/UnitPrice.
        await bus.Received(1).PublishAsync(Arg.Is<BasketCheckedOutIntegrationEvent>(evt =>
            evt.BasketId == basket.Id
            && evt.CustomerId == customerId
            && evt.Items.Count == 1
            && evt.Items[0].ProductId == productId
            && evt.Items[0].Quantity == 2
            && evt.Items[0].UnitPrice == 10m));
    }

    [Fact]
    public async Task Handle_WhenBasketBelongsToAnotherCustomer_ThrowsAndDoesNotCommitOrPublish()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Guid.NewGuid(), "Widget", 10m, 1);
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.CustomerId.Returns(Guid.NewGuid()); // a different customer
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CheckoutHandler.Handle(new CheckoutCommand(basket.Id), repository, identity, unitOfWork, bus, CancellationToken.None));
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<BasketCheckedOutIntegrationEvent>());
    }
}
