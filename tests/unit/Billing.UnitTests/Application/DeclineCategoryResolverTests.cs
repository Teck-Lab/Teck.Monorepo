using Billings.Application.Billing;
using Billings.Application.Billing.Payments;
using Billings.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Billing.UnitTests.Application;

public sealed class DeclineCategoryResolverTests
{
    [Fact]
    public void Resolve_SensitiveProviderCode_AlwaysReturnsGenericDecline()
    {
        var options = Substitute.For<IOptionsMonitor<PaymentProviderOptions>>();
        options.CurrentValue.Returns(new PaymentProviderOptions
        {
            DeclineMappings = new Dictionary<string, string> { ["fraudulent"] = "transient" },
        });

        var result = new DeclineCategoryResolver(options).Resolve("fraudulent");

        Assert.Equal(DeclineCategory.GenericDecline, result.Category);
        Assert.NotEmpty(result.AuditHash);
    }

    [Fact]
    public void Resolve_ChangedOptions_UsesCurrentMappingWithoutRecreation()
    {
        var options = Substitute.For<IOptionsMonitor<PaymentProviderOptions>>();
        options.CurrentValue.Returns(
            new PaymentProviderOptions { DeclineMappings = new Dictionary<string, string> { ["temporary_unavailable"] = "transient" } },
            new PaymentProviderOptions { DeclineMappings = new Dictionary<string, string> { ["temporary_unavailable"] = "issuer-contact-required" } });
        var resolver = new DeclineCategoryResolver(options);

        var first = resolver.Resolve("temporary_unavailable");
        var second = resolver.Resolve("temporary_unavailable");

        Assert.Equal(DeclineCategory.Transient, first.Category);
        Assert.Equal(DeclineCategory.IssuerContactRequired, second.Category);
    }
}
