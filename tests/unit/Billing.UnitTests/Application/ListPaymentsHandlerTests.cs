using Ardalis.Specification;
using Billings.Application.Billing.Payments.Features.ListPayments.V1;
using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using Finbuckle.MultiTenant.Abstractions;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Billing.UnitTests.Application;

public sealed class ListPaymentsHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAllPaymentDtos()
    {
        var a = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), new Money(10m, "USD"));
        var b = Payment.Create("tenant-1", Guid.NewGuid(), Guid.NewGuid(), new Money(20m, "USD"));
        var repository = Substitute.For<IGenericReadRepository<Payment, Guid>>();
        repository.ListAsync(Arg.Any<ISpecification<Payment>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Payment>>([a, b]));

        var result = await ListPaymentsHandler.Handle(new ListPaymentsQuery(), repository, Tenant("tenant-1"), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, dto => dto.Id == a.Id);
        Assert.Contains(result, dto => dto.Id == b.Id);
    }

    private static ITenantInfo Tenant(string tenantId)
    {
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns(tenantId);
        return tenant;
    }
}
