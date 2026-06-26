namespace SharedKernel.Core.CQRS;

/// <summary>
/// Query interface with a response. Marker interface — WolverineFx discovers handlers by convention.
/// </summary>
/// <typeparam name="T">The type of the response returned by the query's handler.</typeparam>
public interface IQuery<out T>
    where T : notnull
{
}
