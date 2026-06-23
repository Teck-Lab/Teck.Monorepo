namespace SharedKernel.Core.CQRS;

/// <summary>
/// Command handler with no response.
/// </summary>
public interface ICommandHandler<in TCommand>
    : ICommandHandler<TCommand, Unit>
    where TCommand : ICommand<Unit>
{
}

/// <summary>
/// Command handler with response. WolverineFx discovers handlers implementing this interface
/// or by convention (any public Handle method accepting the message type).
/// </summary>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : notnull
{
    Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken = default);
}
