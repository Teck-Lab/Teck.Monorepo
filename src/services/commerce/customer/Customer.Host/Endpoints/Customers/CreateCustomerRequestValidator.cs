using FastEndpoints;
using FluentValidation;

namespace Customers.Host.Endpoints.Customers;

/// <summary>Validates <see cref="CreateCustomerRequest"/> instances.</summary>
public sealed class CreateCustomerRequestValidator : Validator<CreateCustomerRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CreateCustomerRequestValidator"/> class.</summary>
    public CreateCustomerRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress();
        RuleFor(request => request.FirstName).NotEmpty();
        RuleFor(request => request.LastName).NotEmpty();
    }
}
