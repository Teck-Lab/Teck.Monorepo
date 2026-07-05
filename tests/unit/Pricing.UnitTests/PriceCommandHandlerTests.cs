using ErrorOr;
using NSubstitute;
using Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PriceCommandHandlerTests
{
    [Fact]
    public async Task AddOrUpdatePrice_OnActiveList_AddsPrice_AndPublishes()
    {
        var list = PriceList.Create("l", new PriceScope("USD", null, null, null), null, null, "tenant-1");
        list.Activate();
        var repo = Substitute.For<IGenericWriteRepository<PriceList, Guid>>();
        var priceRepo = Substitute.For<IGenericWriteRepository<Price, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<PriceList>>(), true, Arg.Any<CancellationToken>())
            .Returns(list);

        var command = new AddOrUpdatePriceCommand(list.Id, Guid.NewGuid(), 10m, [new PriceTierInput(10, 8m)]);
        var result = await AddOrUpdatePriceHandler.Handle(command, repo, priceRepo, uow, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(list.Prices);
        await priceRepo.Received(1).AddAsync(Arg.Any<Price>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<PriceChangedIntegrationEvent>());
    }

    [Fact]
    public async Task AddOrUpdatePrice_MissingList_ReturnsNotFound()
    {
        var repo = Substitute.For<IGenericWriteRepository<PriceList, Guid>>();
        var priceRepo = Substitute.For<IGenericWriteRepository<Price, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<PriceList>>(), true, Arg.Any<CancellationToken>())
            .Returns((PriceList?)null);

        var result = await AddOrUpdatePriceHandler.Handle(
            new AddOrUpdatePriceCommand(Guid.NewGuid(), Guid.NewGuid(), 10m, []), repo, priceRepo, uow, bus, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }
}
