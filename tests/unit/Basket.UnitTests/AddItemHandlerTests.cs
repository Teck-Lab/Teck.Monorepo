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
        const string subject = "shopper-subject";
        var basket = Basket.CreateForSubject(subject, "tenant-1");
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.Subject.Returns(subject);
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var command = new AddItemCommand(basket.Id, Guid.NewGuid(), "Widget", 2);
        var dto = await AddItemHandler.Handle(command, repository, identity, unitOfWork, CancellationToken.None);

        Assert.Single(dto.Items);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenBasketBelongsToAnotherCustomer_ThrowsAndDoesNotCommit()
    {
        var basket = Basket.CreateForSubject("owner-subject", "tenant-1");
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(basket));
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.Subject.Returns("different-subject");

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var command = new AddItemCommand(basket.Id, Guid.NewGuid(), "Widget", 2);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            AddItemHandler.Handle(command, repository, identity, unitOfWork, CancellationToken.None));
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
