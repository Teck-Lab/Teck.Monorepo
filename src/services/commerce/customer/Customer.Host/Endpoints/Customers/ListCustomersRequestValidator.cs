using FastEndpoints;

namespace Customers.Host.Endpoints.Customers;

/// <summary>Validates <see cref="ListCustomersRequest"/> instances.</summary>
public sealed class ListCustomersRequestValidator : Validator<ListCustomersRequest>
{
    /// <summary>Initializes a new instance of the <see cref="ListCustomersRequestValidator"/> class.</summary>
    public ListCustomersRequestValidator()
    {
    }
}
