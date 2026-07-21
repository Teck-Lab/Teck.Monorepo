using FastEndpoints;
using FluentValidation;

namespace Billings.Host.Endpoints.Payments;

/// <summary>Validates <see cref="CapturePaymentRequest"/> instances.</summary>
public sealed class CapturePaymentRequestValidator : Validator<CapturePaymentRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CapturePaymentRequestValidator"/> class.</summary>
    public CapturePaymentRequestValidator()
    {
        RuleFor(request => request.OrderId).NotEmpty();
        RuleFor(request => request.CustomerId).NotEmpty();
        RuleFor(request => request.Amount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Currency).NotEmpty().Length(3);
    }
}
