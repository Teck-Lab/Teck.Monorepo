using Ardalis.Specification;
using Baskets.Application.Baskets;
using Baskets.Application.Baskets.Features.GetOrCreateBasket.V1;
using Baskets.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Baskets.UnitTests;

public sealed class GetOrCreateBasketHandlerTests
{
    [Fact]
    public async Task Handle_CustomerWithNoBasket_CreatesAndCommits()
    {
        var repository = Substitute.For<IGenericWriteRepository<Basket, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Basket>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Basket?>(null));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.CustomerId.Returns(Guid.NewGuid());
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");

        var dto = await GetOrCreateBasketHandler.Handle(
            new GetOrCreateBasketCommand(), repository, unitOfWork, identity, tenant, CancellationToken.None);

        await repository.Received(1).AddAsync(Arg.Any<Basket>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal("Active", dto.Status);
    }
}
