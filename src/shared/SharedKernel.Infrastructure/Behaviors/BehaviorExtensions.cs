using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Infrastructure.Messaging.Idempotency;
using Wolverine;

namespace SharedKernel.Infrastructure.Behaviors;

/// <summary>
/// Registers WolverineFx middleware behaviors.
/// Transactional behavior, validation, and logging are handled by WolverineFx built-in
/// features (AutoApplyTransactions, UseFluentValidation, built-in logging).
/// Only custom behaviors like LicenseEnforcementMiddleware are registered here.
/// </summary>
public static class BehaviorExtensions
{
    /// <summary>
    /// Registers Teck custom WolverineFx middleware behaviors (idempotency and license enforcement).
    /// </summary>
    /// <param name="opts">The WolverineFx options to configure.</param>
    /// <returns>The same <see cref="WolverineOptions"/> instance for fluent chaining.</returns>
    public static WolverineOptions AddTeckBehaviors(this WolverineOptions opts)
    {
        opts.Services.AddSingleton<IdempotencyMiddleware>();
        opts.Policies.AddMiddleware<IdempotencyMiddleware>();

        opts.Services.AddSingleton<LicenseEnforcementMiddleware>();
        opts.Policies.AddMiddleware<LicenseEnforcementMiddleware>();

        return opts;
    }
}
