using Ardalis.Specification;
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
        var basket = Basket.CreateForCustomer(Guid.NewGuid(), "tenant-1");
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var command = new AddItemCommand(basket.Id, Guid.NewGuid(), "Widget", 10m, 2);
        var dto = await AddItemHandler.Handle(command, repository, unitOfWork, CancellationToken.None);

        Assert.Single(dto.Items);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
