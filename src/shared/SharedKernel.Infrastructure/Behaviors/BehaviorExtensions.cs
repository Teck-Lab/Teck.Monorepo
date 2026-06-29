using ErrorOr;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
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
    /// Each middleware is activated in the Wolverine pipeline only when its required dependency
    /// (<see cref="IDatabase"/> for idempotency; <see cref="ILicenseValidator"/> for licensing)
    /// is already registered in the DI container. Services that do not register those deps keep
    /// the middlewares dormant and boot normally.
    /// </summary>
    /// <param name="opts">The WolverineFx options to configure.</param>
    /// <returns>The same <see cref="WolverineOptions"/> instance for fluent chaining.</returns>
    public static WolverineOptions AddTeckBehaviors(this WolverineOptions opts)
    {
        // Per-service EF Core registrations (e.g. IUnitOfWork, IGenericWriteRepository<,>) are
        // registered as lambda factories in each service host so that the scoped write DbContext
        // is shared within a single request rather than created twice. Wolverine 6.x changed the
        // default ServiceLocationPolicy to NotAllowed, which rejects handlers that depend on such
        // opaque registrations. AllowedButWarn restores the pre-6.0 behaviour: handlers compile
        // using service-location for opaque dependencies and a warning is emitted during startup.
        opts.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

        // Production hardening for WolverineFx's runtime code generation. The container build runs
        // `codegen write` to emit handler source ahead of time (see deploy/AGENTS.md). In production
        // we then load those pre-generated types statically instead of compiling handlers at runtime
        // (faster cold start, no Roslyn dependency) and fail fast on startup if any expected type is
        // missing — which catches an image that was published without the codegen step. Development
        // keeps the default dynamic, auto-generating mode for a frictionless inner loop.
        opts.Services.CritterStackDefaults(critter =>
        {
            critter.Production.GeneratedCodeMode = TypeLoadMode.Static;
            critter.Production.AssertAllPreGeneratedTypesExist = true;
        });

        // Register ErrorOr<T> as a Wolverine result type so that handlers returning
        // Task<ErrorOr<T>> are correctly handled by InvokeAsync<T>(). Without this,
        // the ErrorOr<T> wrapper is never matched against the expected response type
        // (typeof(T)), leaving envelope.Response null and causing callers to receive
        // null (i.e. an empty 200 body with no JSON content). With this registration:
        //   - On error  → Wolverine logs each error description and returns null.
        //   - On success → Wolverine unwraps the inner T and cascades it as the
        //                  response so InvokeAsync<T>() receives the actual value.
        opts.UseResultType(
            typeof(ErrorOr<>),
            stopWhen: static result => ((IErrorOr)result).IsError,
            unwrapWith: static result => result is IErrorOr<object> g ? (object?)g.Value : null,
            errorsFrom: static result =>
                ((IErrorOr)result).Errors?.Select(static e => e.Description)
                    ?? Enumerable.Empty<string>(),
            unwrappedArgumentIndex: 0);

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

        if (opts.Services.Any(d => d.ServiceType == typeof(IDatabase)))
        {
            opts.Policies.AddMiddleware<IdempotencyMiddleware>();
        }

        opts.Services.AddSingleton<LicenseEnforcementMiddleware>(static sp =>
            new LicenseEnforcementMiddleware(
                sp.GetRequiredService<ILicenseValidator>()));

        if (opts.Services.Any(d => d.ServiceType == typeof(ILicenseValidator)))
        {
            opts.Policies.AddMiddleware<LicenseEnforcementMiddleware>();
        }

        return opts;
    }
}
