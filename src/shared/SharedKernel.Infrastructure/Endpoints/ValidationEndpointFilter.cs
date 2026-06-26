using ErrorOr;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Validates Minimal API arguments using registered FluentValidation validators
/// and returns a centralized ProblemDetails response when validation fails.
/// </summary>
public sealed class ValidationEndpointFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var errors = new List<Error>();

        foreach (object? argument in context.Arguments)
        {
            if (argument is null || argument is HttpContext || argument is CancellationToken)
            {
                continue;
            }

            Type argumentType = argument.GetType();
            Type validatorType = typeof(IValidator<>).MakeGenericType(argumentType);
            object? validatorService = context.HttpContext.RequestServices.GetService(validatorType);
            IValidator? validator = validatorService as IValidator;

            if (validator is null)
            {
                continue;
            }

            IValidationContext validationContext = new ValidationContext<object>(argument);
            ValidationResult validationResult = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (validationResult.IsValid)
            {
                continue;
            }

            errors.AddRange(validationResult.Errors
                .Where(failure => failure is not null)
                .Select(failure => Error.Validation(
                    code: failure.PropertyName,
                    description: failure.ErrorMessage)));
        }

        if (errors.Count > 0)
        {
            return errors.ToMinimalApiErrorResult(context.HttpContext);
        }

        return await next(context);
    }
}
