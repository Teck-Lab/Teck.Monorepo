using FastEndpoints;
using FluentValidation;

namespace Billings.Host.Endpoints.Invoices;

/// <summary>Validates <see cref="GetInvoiceRequest"/> instances.</summary>
public sealed class GetInvoiceRequestValidator : Validator<GetInvoiceRequest>
{
    /// <summary>Initializes a new instance of the <see cref="GetInvoiceRequestValidator"/> class.</summary>
    public GetInvoiceRequestValidator() => RuleFor(request => request.InvoiceId).NotEmpty();
}
