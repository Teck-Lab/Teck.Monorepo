using FluentValidation;

namespace SharedKernel.Infrastructure.Validation;

/// <summary>
/// Represents a request that supports paging.
/// </summary>
public interface IPaginationRequest
{
    /// <summary>
    /// Gets or sets the one-based page number to retrieve.
    /// </summary>
    int Page { get; set; }

    /// <summary>
    /// Gets or sets the number of items to retrieve per page.
    /// </summary>
    int Size { get; set; }
}

/// <summary>
/// Represents a request identified by a <see cref="Guid"/>.
/// </summary>
public interface IGuidRequest
{
    /// <summary>
    /// Gets or sets the identifier of the request.
    /// </summary>
    Guid Id { get; set; }
}

/// <summary>
/// Validates that a paginated request has a valid page number and page size.
/// </summary>
/// <typeparam name="TRequest">The pagination request type to validate.</typeparam>
public sealed class PaginationValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : IPaginationRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaginationValidator{TRequest}"/> class.
    /// </summary>
    public PaginationValidator()
    {
        RuleFor(request => request.Page).GreaterThan(0);
        RuleFor(request => request.Size).InclusiveBetween(1, 100);
    }
}
