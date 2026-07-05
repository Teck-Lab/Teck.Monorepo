using Ardalis.Specification;
using Baskets.Application.Baskets;
using Baskets.Application.Baskets.Features.AddItem.V1;
using Baskets.Domain.Entities;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Baskets.UnitTests;

public sealed class AddItemHandlerTests
{
    [Fact]
    public async Task Handle_AddsItemAndCommits()
    {
        var customerId = Guid.NewGuid();
        var basket = Basket.CreateForCustomer(customerId, "tenant-1");
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.CustomerId.Returns(customerId);
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var command = new AddItemCommand(basket.Id, Guid.NewGuid(), "Widget", 10m, 2);
        var dto = await AddItemHandler.Handle(command, repository, identity, unitOfWork, CancellationToken.None);

        Assert.Single(dto.Items);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenBasketBelongsToAnotherCustomer_ThrowsAndDoesNotCommit()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.CustomerId.Returns(Guid.NewGuid()); // a different customer

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var command = new AddItemCommand(basket.Id, Guid.NewGuid(), "Widget", 10m, 2);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            AddItemHandler.Handle(command, repository, identity, unitOfWork, CancellationToken.None));
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
