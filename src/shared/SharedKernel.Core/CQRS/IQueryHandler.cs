namespace SharedKernel.Core.CQRS;

/// <summary>
/// Query handler with response. WolverineFx discovers handlers implementing this interface
/// or by convention (any public Handle method accepting the query type).
/// </summary>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
    where TResponse : notnull
{
    Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken = default);
}
