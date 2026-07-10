using ErrorOr;
using Finbuckle.MultiTenant.Abstractions;
using NSubstitute;
using Pricing.Application.Pricing.Features.ActivatePriceList.V1;
using Pricing.Application.Pricing.Features.CreatePriceList.V1;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceListHandlerTests
{
    [Fact]
    public async Task Create_AddsDraftList_AndCommitsOnce()
    {
        var repo = Substitute.For<IGenericWriteRepository<PriceList, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");

        var command = new CreatePriceListCommand("Retail", null, "USD", null, null, null, null, null);

        var dto = await CreatePriceListHandler.Handle(command, repo, uow, tenant, bus, CancellationToken.None);

        Assert.Equal("Draft", dto.Status);
        await repo.Received(1).AddAsync(Arg.Any<PriceList>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Activate_MissingList_ReturnsNotFound()
    {
        var repo = Substitute.For<IGenericWriteRepository<PriceList, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<PriceList>>(), true, Arg.Any<CancellationToken>())
            .Returns((PriceList?)null);

        var result = await ActivatePriceListHandler.Handle(new ActivatePriceListCommand(Guid.NewGuid()), repo, uow, bus, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Activate_PublishesPriceChangedPerPrice()
    {
        var list = PriceList.Create("l", new PriceScope("USD", null, null, null), null, null, "tenant-1");
        list.AddOrUpdatePrice(Guid.NewGuid(), new Money(10m, "USD"), []);
        var repo = Substitute.For<IGenericWriteRepository<PriceList, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<PriceList>>(), true, Arg.Any<CancellationToken>())
            .Returns(list);

        var result = await ActivatePriceListHandler.Handle(new ActivatePriceListCommand(list.Id), repo, uow, bus, CancellationToken.None);

        Assert.False(result.IsError);
        await bus.Received(1).PublishAsync(Arg.Any<PriceChangedIntegrationEvent>());
    }
}
