using FastEndpoints;
using FluentValidation;

namespace Customers.Host.Endpoints.Customers;

/// <summary>Validates <see cref="AddCustomerAddressRequest"/> instances.</summary>
public sealed class AddCustomerAddressRequestValidator : Validator<AddCustomerAddressRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AddCustomerAddressRequestValidator"/> class.</summary>
    public AddCustomerAddressRequestValidator()
    {
        RuleFor(request => request.CustomerId).NotEmpty();
        RuleFor(request => request.Line1).NotEmpty();
        RuleFor(request => request.City).NotEmpty();
        RuleFor(request => request.PostalCode).NotEmpty();
        RuleFor(request => request.Country).NotEmpty();
    }
}
