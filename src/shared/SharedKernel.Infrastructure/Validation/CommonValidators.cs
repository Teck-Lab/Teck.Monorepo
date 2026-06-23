using FluentValidation;

namespace SharedKernel.Infrastructure.Validation;

public interface IPaginationRequest
{
    int Page { get; set; }

    int Size { get; set; }
}

public interface IGuidRequest
{
    Guid Id { get; set; }
}

public sealed class PaginationValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : IPaginationRequest
{
    public PaginationValidator()
    {
        RuleFor(request => request.Page).GreaterThan(0);
        RuleFor(request => request.Size).InclusiveBetween(1, 100);
    }
}

public sealed class GuidValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : IGuidRequest
{
    public GuidValidator()
    {
        RuleFor(request => request.Id).NotEmpty();
    }
}
