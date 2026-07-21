using Ardalis.Specification;
using Billings.Application.Billing.Invoices.Features.GetInvoice.V1;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Billing.UnitTests.Application;

public sealed class GetInvoiceHandlerTests
{
    [Fact]
    public async Task Handle_WhenFound_ReturnsDto()
    {
        var total = new Money(30m, "USD");
        var line = new InvoiceLineInput(Guid.NewGuid(), "Widget", 3, new Money(10m, "USD"));
        var invoice = Invoice.Create("tenant-1", Guid.NewGuid(), total, [line], DateTimeOffset.UtcNow);
        var repository = Substitute.For<IGenericReadRepository<Invoice, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Invoice>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Invoice?>(invoice));

        var result = await GetInvoiceHandler.Handle(new GetInvoiceQuery(invoice.Id), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(invoice.Id, result.Value.Id);
        Assert.Equal(30m, result.Value.Amount);
        Assert.Equal("USD", result.Value.Currency);
        var onlyLine = Assert.Single(result.Value.Lines);
        Assert.Equal(3, onlyLine.Quantity);
        Assert.Equal(10m, onlyLine.UnitPriceAmount);
    }

    [Fact]
    public async Task Handle_WhenMissing_ReturnsNotFound()
    {
        var repository = Substitute.For<IGenericReadRepository<Invoice, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Invoice>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Invoice?>(null));

        var result = await GetInvoiceHandler.Handle(new GetInvoiceQuery(Guid.NewGuid()), repository, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
