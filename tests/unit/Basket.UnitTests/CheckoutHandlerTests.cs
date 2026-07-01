using Ardalis.Specification;
using Baskets.Application.Baskets.Features.Checkout.V1;
using Baskets.Domain.Entities;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Baskets.UnitTests;

public sealed class CheckoutHandlerTests
{
    [Fact]
    public async Task Handle_ChecksOutBasketAndCommits()
    {
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        basket.AddItem(Guid.NewGuid(), "Widget", 10m, 1);
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var dto = await CheckoutHandler.Handle(new CheckoutCommand(basket.Id), repository, unitOfWork, CancellationToken.None);

        Assert.Equal("CheckedOut", dto.Status);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
