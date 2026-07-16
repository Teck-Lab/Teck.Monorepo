using Ardalis.Specification;
using Billings.Application.Billing.Payments.Features.GetPayment.V1;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Billing.UnitTests.Application;

public sealed class GetPaymentHandlerTests
{
    [Fact]
    public async Task Handle_WhenFound_ReturnsDto()
    {
        var payment = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), new Money(20m, "USD"));
        var repository = Substitute.For<IGenericReadRepository<Payment, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Payment?>(payment));

        var result = await GetPaymentHandler.Handle(new GetPaymentQuery(payment.Id), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(payment.Id, result.Value.Id);
        Assert.Equal("Pending", result.Value.Status);
    }

    [Fact]
    public async Task Handle_WhenMissing_ReturnsNotFound()
    {
        var repository = Substitute.For<IGenericReadRepository<Payment, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Payment?>(null));

        var result = await GetPaymentHandler.Handle(new GetPaymentQuery(Guid.NewGuid()), repository, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
