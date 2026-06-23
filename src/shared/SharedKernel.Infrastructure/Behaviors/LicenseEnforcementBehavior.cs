using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using ErrorOr;
using SharedKernel.Core.CQRS;
using SharedKernel.Core.Licensing;
using Wolverine;

namespace SharedKernel.Infrastructure.Behaviors;

/// <summary>
/// WolverineFx middleware that enforces license validation for requests
/// implementing <see cref="ILicenseGatedRequest"/>.
/// </summary>
public sealed class LicenseEnforcementMiddleware(
    ILicenseValidator licenseValidator)
{
    private static readonly MethodInfo? _fromMethod = typeof(ErrorOr<object>).GetMethod(
        nameof(ErrorOr<object>.From),
        BindingFlags.Public | BindingFlags.Static,
        [typeof(List<Error>)]);

    public async ValueTask InvokeAsync(
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
