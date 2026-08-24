using FastEndpoints;
using FluentValidation;

namespace Orders.Host.Endpoints.Orders;

/// <summary>Validates owner-authorized payment retry requests.</summary>
public sealed class RetryPaymentRequestValidator : Validator<RetryPaymentRequest>
{
    /// <summary>Initializes a new instance of the <see cref="RetryPaymentRequestValidator"/> class.</summary>
    public RetryPaymentRequestValidator()
    {
        RuleFor(request => request.OrderId).NotEmpty();
        RuleFor(request => request.RequestId).NotEmpty().MaximumLength(128);
        RuleFor(request => request.PaymentMethodToken).NotEmpty().MaximumLength(256);
    }
}
