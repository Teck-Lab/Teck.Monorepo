namespace SharedKernel.Core.CQRS;

/// <summary>
/// Command handler with no response.
/// </summary>
/// <typeparam name="TCommand">The command type handled by this handler.</typeparam>
public interface ICommandHandler<in TCommand>
    : ICommandHandler<TCommand, Unit>
    where TCommand : ICommand<Unit>
{
}

/// <summary>
/// Command handler with response. WolverineFx discovers handlers implementing this interface
/// or by convention (any public Handle method accepting the message type).
/// </summary>
/// <typeparam name="TCommand">The command type handled by this handler.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : notnull
{
    /// <summary>
    /// Handles the specified command and produces a response.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="Task{TResult}"/> that resolves to the command's response.</returns>
    Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken = default);
}
