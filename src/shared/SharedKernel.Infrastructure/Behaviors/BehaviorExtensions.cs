using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Licensing;
using SharedKernel.Infrastructure.Messaging.Idempotency;
using StackExchange.Redis;
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
        // Factory-delegate registrations are used here intentionally: IdempotencyMiddleware
        // depends on IDatabase (Redis) and LicenseEnforcementMiddleware depends on ILicenseValidator,
        // neither of which is required at DI build time by all service hosts. Using a factory
        // bypasses the ValidateOnBuild singleton-dependency check so handler-only services
        // (e.g. Customer.Host) can start without Redis or a license validator — these middlewares
        // are only invoked when Wolverine processes durable messages, which such services never do.
        opts.Services.AddSingleton<IdempotencyMiddleware>(static sp =>
            new IdempotencyMiddleware(
                sp.GetRequiredService<ILogger<IdempotencyMiddleware>>(),
                sp.GetRequiredService<IDatabase>()));
        opts.Policies.AddMiddleware<IdempotencyMiddleware>();

        opts.Services.AddSingleton<LicenseEnforcementMiddleware>(static sp =>
            new LicenseEnforcementMiddleware(
                sp.GetRequiredService<ILicenseValidator>()));
        opts.Policies.AddMiddleware<LicenseEnforcementMiddleware>();

        return opts;
    }
}
