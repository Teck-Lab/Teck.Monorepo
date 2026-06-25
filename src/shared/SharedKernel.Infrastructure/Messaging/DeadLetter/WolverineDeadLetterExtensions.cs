using Wolverine;
using Wolverine.ErrorHandling;

namespace SharedKernel.Infrastructure.Messaging.DeadLetter;

/// <summary>
/// Wolverine dead letter policy extensions.
/// </summary>
public static class WolverineDeadLetterExtensions
{
    /// <summary>
    /// Configures Teck dead letter handling for Wolverine.
    /// </summary>
    /// <param name="opts">The Wolverine options.</param>
    /// <param name="options">The dead letter options.</param>
    /// <returns>The configured Wolverine options.</returns>
    public static WolverineOptions AddTeckDeadLetterPolicy(this WolverineOptions opts, DeadLetterOptions options)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return opts;
        }

        opts.Policies.OnException<Exception>()
            .RetryWithCooldown(Enumerable.Repeat(TimeSpan.FromSeconds(5), options.MaxRetries).ToArray())
            .Then.MoveToErrorQueue();

        return opts;
    }
}
