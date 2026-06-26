using FluentValidation;

namespace SharedKernel.Infrastructure.Validation;

/// <summary>
/// Validates that a request's identifier is a non-empty <see cref="Guid"/>.
/// </summary>
/// <typeparam name="TRequest">The request type to validate.</typeparam>
public sealed class GuidValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : IGuidRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GuidValidator{TRequest}"/> class.
    /// </summary>
    public GuidValidator()
    {
        RuleFor(request => request.Id).NotEmpty();
    }
}
