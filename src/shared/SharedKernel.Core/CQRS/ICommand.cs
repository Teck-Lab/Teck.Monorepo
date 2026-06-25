namespace SharedKernel.Core.CQRS;

/// <summary>
/// Command with no response. Marker interface — WolverineFx discovers handlers by convention.
/// </summary>
public interface ICommand : ICommand<Unit>
{
}

/// <summary>
/// Command with response. Marker interface — WolverineFx discovers handlers by convention.
/// </summary>
/// <typeparam name="TResponse">The type of the response returned by the command's handler.</typeparam>
public interface ICommand<out TResponse>
{
}

/// <summary>
/// Transactional command with no response.
/// </summary>
public interface ITransactionalCommand : ICommand<Unit>
{
}

/// <summary>
/// Transactional command with a response.
/// </summary>
/// <typeparam name="TResponse">The type of the response returned by the command's handler.</typeparam>
public interface ITransactionalCommand<out TResponse> : ICommand<TResponse>
{
}
