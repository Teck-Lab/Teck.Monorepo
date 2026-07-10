using ErrorOr;
using Finbuckle.MultiTenant.Abstractions;
using NSubstitute;
using Pricing.Application.Pricing.Features.SetExchangeRate.V1;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using Xunit;

namespace Pricing.UnitTests;

public sealed class ExchangeRateHandlerTests
{
    [Fact]
    public async Task Set_NewPair_CreatesRate()
    {
        var repo = Substitute.For<IGenericWriteRepository<ExchangeRate, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<ExchangeRate>>(), true, Arg.Any<CancellationToken>())
            .Returns((ExchangeRate?)null);

        var dto = await SetExchangeRateHandler.Handle(
            new SetExchangeRateCommand("USD", "EUR", 0.9m, null, null), repo, uow, tenant, CancellationToken.None);

        Assert.Equal(0.9m, dto.Rate);
        await repo.Received(1).AddAsync(Arg.Any<ExchangeRate>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_ExistingPair_UpdatesInPlace()
    {
        var existing = ExchangeRate.Create("USD", "EUR", 0.8m, null, null, "tenant-1");
        var repo = Substitute.For<IGenericWriteRepository<ExchangeRate, Guid>>();
        var uow = Substitute.For<IUnitOfWork>();
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<ExchangeRate>>(), true, Arg.Any<CancellationToken>())
            .Returns(existing);

        var dto = await SetExchangeRateHandler.Handle(
            new SetExchangeRateCommand("USD", "EUR", 0.95m, null, null), repo, uow, tenant, CancellationToken.None);

        Assert.Equal(0.95m, dto.Rate);
        await repo.DidNotReceive().AddAsync(Arg.Any<ExchangeRate>(), Arg.Any<CancellationToken>());
    }
}
