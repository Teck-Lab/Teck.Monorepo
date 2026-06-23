namespace SharedKernel.Core.CQRS;

/// <summary>
/// Query interface with a response. Marker interface — WolverineFx discovers handlers by convention.
/// </summary>
public interface IQuery<out T>
    where T : notnull
{
}
