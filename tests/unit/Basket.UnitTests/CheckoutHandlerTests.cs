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
    public async Task Handle_BeginsPricingCommitsAndPublishesCheckoutPricingRequest()
    {
        const string subject = "shopper-subject";
        var productId = Guid.NewGuid();
        var basket = Basket.CreateForSubject(subject, "tenant-1");
        basket.AddItem(productId, "Widget", 2);
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.Subject.Returns(subject);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        var dto = await CheckoutHandler.Handle(new CheckoutCommand(basket.Id, 25m, "USD", "tok_test_123"), repository, identity, unitOfWork, bus, CancellationToken.None);

        Assert.Equal("PricingPending", dto.Status);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await bus.Received(1).PublishAsync(Arg.Is<BasketCheckoutRequestedIntegrationEvent>(evt =>
            evt.BasketId == basket.Id
            && evt.AuthorizedAmount == 25m
            && evt.Currency == "USD"
            && evt.Lines.Count == 1
            && evt.Lines[0].ProductId == productId
            && evt.Lines[0].Quantity == 2));
    }

    [Fact]
    public async Task Handle_WhenBasketBelongsToAnotherCustomer_ThrowsAndDoesNotCommitOrPublish()
    {
        var basket = Basket.CreateForSubject("owner-subject", "tenant-1");
        basket.AddItem(Guid.NewGuid(), "Widget", 1);
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.Subject.Returns("different-subject");
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CheckoutHandler.Handle(new CheckoutCommand(basket.Id, 25m, "USD", "tok_test_123"), repository, identity, unitOfWork, bus, CancellationToken.None));
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<BasketCheckoutRequestedIntegrationEvent>());
    }
}
