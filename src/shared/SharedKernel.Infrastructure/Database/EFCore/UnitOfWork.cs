using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel.Core.Database;
using SharedKernel.Core.Exceptions;

namespace SharedKernel.Infrastructure.Database.EFCore;

/// <summary>
/// The unit of work.
/// </summary>
/// <typeparam name="TContext">The database context type.</typeparam>
/// <remarks>
/// Initializes a new instance of the <see cref="UnitOfWork{TContext}"/> class.
/// </remarks>
public class UnitOfWork<TContext> : IUnitOfWork
    where TContext : BaseDbContext
{
    private readonly TContext? _context; // For legacy/test usage
    private readonly IDbContextFactory<TContext>? _contextFactory; // For DI usage
    private TContext? _scopedContext; // For per-operation context
    private IDbContextTransaction? _transaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitOfWork{TContext}"/> class with a DbContext instance (legacy/test usage).
    /// </summary>
    /// <param name="context">The database context.</param>
    public UnitOfWork(TContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitOfWork{TContext}"/> class with a DbContext factory (DI usage).
    /// </summary>
    /// <param name="contextFactory">The DbContext factory.</param>
    public UnitOfWork(IDbContextFactory<TContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// Gets the current context, resolving from factory if needed.
    /// </summary>
    private TContext CurrentContext
    {
        get
        {
            if (_context != null)
            {
                return _context;
            }

            if (_scopedContext != null)
            {
                return _scopedContext;
            }

            if (_contextFactory != null)
            {
                _scopedContext = _contextFactory.CreateDbContext();
                return _scopedContext;
            }

            throw new System.InvalidOperationException("No DbContext or DbContextFactory provided to UnitOfWork.");
        }
    }

    /// <summary>
    /// Begins the transaction asynchronously.
    /// </summary>
    /// <param name="isolationLevel">The transaction isolation level.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="InvalidTransactionException">.</exception>
    /// <returns><![CDATA[Task<IDbContextTransaction>]]></returns>
    public async Task<IDbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidTransactionException("A transaction has already been started.");
        }

        _transaction = await CurrentContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
        return _transaction.GetDbTransaction();
    }

    /// <summary>
    /// Commits the transaction asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="InvalidTransactionException">.</exception>
    /// <returns>A Task.</returns>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidTransactionException("A transaction has not been started.");
        }

        await _transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Creates transaction savepoint asynchronously.
    /// </summary>
    /// <param name="savePoint">The name of the savepoint to create.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    public async Task CreateTransactionSavepoint(string savePoint, CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidTransactionException("A transaction has not been started.");
        }

        await _transaction.CreateSavepointAsync(savePoint, cancellationToken);
    }

    /// <summary>
    /// Rollbacks the transaction asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="InvalidTransactionException">.</exception>
    /// <returns>A Task.</returns>
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidTransactionException("A transaction has not been started.");
        }

        await CurrentContext.Database.RollbackTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Rollbacks the transaction to the savepoint asynchronously.
    /// </summary>
    /// <param name="savePoint">The name of the savepoint to roll back to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    public async Task RollbackTransactionToSavepointAsync(string savePoint, CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidTransactionException("A transaction has not been started.");
        }

        await _transaction.RollbackToSavepointAsync(savePoint, cancellationToken);
    }

    /// <summary>
    /// Save the changes asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><![CDATA[Task<int>]]></returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ004:The enumerator returns a reference to the item", Justification = "Add ref when .NET 9 comes out with support for it being async.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "HLQ012:Consider using CollectionsMarshal.AsSpan()", Justification = "Add ref when .NET 9 comes out with support for it being async.")]
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await CurrentContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Dispose.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Virtual dispose.
    /// </summary>
    /// <param name="disposing">A value indicating whether the method is being called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        _transaction?.Dispose();
        if (_scopedContext != null)
        {
            _scopedContext.Dispose();
            _scopedContext = null;
        }

        _context?.Dispose();
    }
}
