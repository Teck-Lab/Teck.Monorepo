using System.Reflection;
using ErrorOr;
using SharedKernel.Core.Licensing;
using Wolverine;

namespace SharedKernel.Infrastructure.Behaviors;

/// <summary>
/// WolverineFx middleware that enforces license validation for requests
/// implementing <see cref="ILicenseGatedRequest"/>.
/// </summary>
/// <param name="licenseValidator">Validator used to verify tenant and location license validity.</param>
public sealed class LicenseEnforcementMiddleware(
    ILicenseValidator licenseValidator)
{
    private static readonly MethodInfo? _fromMethod = typeof(ErrorOr<object>).GetMethod(
        nameof(ErrorOr<object>.From),
        BindingFlags.Public | BindingFlags.Static,
        [typeof(List<Error>)]);

    /// <summary>
    /// Executes before the handler, validating the license for license-gated requests.
    /// </summary>
    /// <param name="context">The WolverineFx message context for the current envelope.</param>
    /// <param name="next">The delegate that invokes the next middleware in the pipeline.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous middleware operation.</returns>
    public async ValueTask BeforeAsync(
        IMessageContext context,
        Func<ValueTask> next,
        CancellationToken cancellationToken)
    {
        if (context.Envelope?.Message is ILicenseGatedRequest gatedRequest)
        {
            LicenseValidationResult validation = await licenseValidator.ValidateAsync(
                gatedRequest.TenantId,
                gatedRequest.LocationId,
                cancellationToken).ConfigureAwait(false);

            if (!validation.IsValid)
            {
                var errors = new List<Error> { Error.Forbidden("License.Enforcement", validation.ErrorMessage ?? "License validation failed.") };
                throw new InvalidOperationException(validation.ErrorMessage ?? "License validation failed.");
            }
        }

        await next();
    }
}
