using FastEndpoints;
using FluentValidation;

namespace Customers.Host.Endpoints.Customers;

/// <summary>Validates <see cref="UpdateCustomerProfileRequest"/> instances.</summary>
public sealed class UpdateCustomerProfileRequestValidator : Validator<UpdateCustomerProfileRequest>
{
    /// <summary>Initializes a new instance of the <see cref="UpdateCustomerProfileRequestValidator"/> class.</summary>
    public UpdateCustomerProfileRequestValidator()
    {
        RuleFor(request => request.CustomerId).NotEmpty();
        RuleFor(request => request.FirstName).NotEmpty();
        RuleFor(request => request.LastName).NotEmpty();
    }
}
