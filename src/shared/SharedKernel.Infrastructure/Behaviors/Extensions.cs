using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using SharedKernel.Infrastructure.Messaging.Idempotency;

namespace SharedKernel.Infrastructure.Behaviors;

/// <summary>
/// Registers WolverineFx middleware behaviors.
/// Transactional behavior, validation, and logging are handled by WolverineFx built-in
/// features (AutoApplyTransactions, UseFluentValidation, built-in logging).
/// Only custom behaviors like LicenseEnforcementMiddleware are registered here.
/// </summary>
public static class BehaviorExtensions
{
    public static WolverineOptions AddTeckBehaviors(this WolverineOptions opts)
    {
        opts.Services.AddSingleton<IdempotencyMiddleware>();
        opts.Policies.AddMiddleware<IdempotencyMiddleware>();

        opts.Services.AddSingleton<LicenseEnforcementMiddleware>();
        opts.Policies.AddMiddleware<LicenseEnforcementMiddleware>();

        return opts;
    }
}
