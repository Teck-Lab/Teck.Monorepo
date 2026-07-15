using Ardalis.Specification;
using Finbuckle.MultiTenant.Abstractions;
using Inventories.Application.Inventory.Features.SetLocationPriorities.V1;
using Inventories.Domain.Entities;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Inventories.UnitTests;

/// <summary>Tests for <see cref="SetLocationPrioritiesHandler"/>.</summary>
public sealed class SetLocationPrioritiesHandlerTests
{
    [Fact]
    public async Task Handle_NoExistingPriorityForTenant_CreatesAndCommitsOnce()
    {
        var repository = Substitute.For<IGenericWriteRepository<LocationPriority, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<LocationPriority>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LocationPriority?>(null));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");
        var ordered = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var dto = await SetLocationPrioritiesHandler.Handle(
            new SetLocationPrioritiesCommand(ordered), repository, unitOfWork, tenant, CancellationToken.None);

        Assert.Equal(ordered, dto.LocationIds);
        await repository.Received(1).AddAsync(
            Arg.Is<LocationPriority>(priority => priority.TenantId == "tenant-1" && priority.LocationIds.SequenceEqual(ordered)),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingPriorityForTenant_UpdatesInPlaceAndCommitsOnceWithoutDuplicate()
    {
        var existing = LocationPriority.Create("tenant-1", new List<Guid> { Guid.NewGuid() });
        var repository = Substitute.For<IGenericWriteRepository<LocationPriority, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<LocationPriority>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LocationPriority?>(existing));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");
        var ordered = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var dto = await SetLocationPrioritiesHandler.Handle(
            new SetLocationPrioritiesCommand(ordered), repository, unitOfWork, tenant, CancellationToken.None);

        Assert.Equal(existing.Id, dto.Id);
        Assert.Equal(ordered, dto.LocationIds);
        await repository.DidNotReceive().AddAsync(Arg.Any<LocationPriority>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
