using System.Linq.Expressions;
using Ardalis.Specification;
using Customers.Domain.Entities;
using SharedKernel.Core.Database;

namespace Customer.UnitTests.Grpc;

/// <summary>In-memory fake tenant read repository for unit tests.</summary>
internal sealed class FakeTenantReadRepository : IGenericReadRepository<Tenant, Guid>
{
    private readonly Tenant? seeded;

    /// <summary>Initializes a new instance of <see cref="FakeTenantReadRepository"/> with an optional seeded tenant.</summary>
    /// <param name="seeded">The tenant to return from <see cref="FirstOrDefaultAsync(ISpecification{Tenant}, CancellationToken)"/>, or <see langword="null"/>.</param>
    internal FakeTenantReadRepository(Tenant? seeded) => this.seeded = seeded;

    /// <inheritdoc/>
    public Task<Tenant?> FirstOrDefaultAsync(ISpecification<Tenant> specification, CancellationToken cancellationToken = default)
        => Task.FromResult(seeded);

    /// <inheritdoc/>
    public Task<Tenant?> FirstOrDefaultAsync(ISpecification<Tenant> specification, bool enableTracking, CancellationToken cancellationToken = default)
        => Task.FromResult(seeded);

    /// <inheritdoc/>
    public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<Tenant, TResult> specification, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<Tenant?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<Tenant>> FindByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<Tenant?> FindOneAsync(Expression<Func<Tenant, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<Tenant>> FindAsync(Expression<Func<Tenant, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<Tenant>> GetAllAsync(bool enableTracking = false, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(Expression<Func<Tenant, bool>> predicate, bool enableTracking = false, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<Tenant>> ListAsync(ISpecification<Tenant> specification, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<Tenant>> ListAsync(ISpecification<Tenant> specification, bool enableTracking, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<TResult>> ListAsync<TResult>(ISpecification<Tenant, TResult> specification, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<int> CountAsync(ISpecification<Tenant> specification, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<bool> AnyAsync(ISpecification<Tenant> specification, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
