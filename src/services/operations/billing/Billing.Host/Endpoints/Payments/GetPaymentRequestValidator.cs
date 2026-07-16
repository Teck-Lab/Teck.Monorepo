using FastEndpoints;
using FluentValidation;

namespace Billings.Host.Endpoints.Payments;

/// <summary>Validates <see cref="GetPaymentRequest"/> instances.</summary>
public sealed class GetPaymentRequestValidator : Validator<GetPaymentRequest>
{
    /// <summary>Initializes a new instance of the <see cref="GetPaymentRequestValidator"/> class.</summary>
    public GetPaymentRequestValidator() => RuleFor(request => request.PaymentId).NotEmpty();
}
