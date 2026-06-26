namespace SharedKernel.Core.CQRS;

/// <summary>
/// Query handler with response. WolverineFx discovers handlers implementing this interface
/// or by convention (any public Handle method accepting the query type).
/// </summary>
/// <typeparam name="TQuery">The query type handled by this handler.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
    where TResponse : notnull
{
    /// <summary>
    /// Handles the specified query and produces a response.
    /// </summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="Task{TResult}"/> that resolves to the query's response.</returns>
    Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken = default);
}
