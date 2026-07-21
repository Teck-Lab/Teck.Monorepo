using FastEndpoints;
using FluentValidation;

namespace Customers.Host.Endpoints.Customers;

/// <summary>Validates <see cref="GetCustomerRequest"/> instances.</summary>
public sealed class GetCustomerRequestValidator : Validator<GetCustomerRequest>
{
    /// <summary>Initializes a new instance of the <see cref="GetCustomerRequestValidator"/> class.</summary>
    public GetCustomerRequestValidator() => RuleFor(request => request.CustomerId).NotEmpty();
}
