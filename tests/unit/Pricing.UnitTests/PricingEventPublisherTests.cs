using NSubstitute;
using Pricing.Application.Pricing;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.ValueObjects;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Pricing.UnitTests;

public sealed class PricingEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_PublishesOneIntegrationEventPerDomainEvent()
    {
        var bus = Substitute.For<IMessageBus>();
        var evt = new PriceChanged(Guid.NewGuid(), Guid.NewGuid(), "tenant-1", 10m, "USD", DateTimeOffset.UtcNow, PriceChangeType.Upserted);

        await PricingEventPublisher.PublishAsync([evt], bus);

        await bus.Received(1).PublishAsync(Arg.Is<PriceChangedIntegrationEvent>(e =>
            e.ProductId == evt.ProductId && e.ChangeType == "Upserted" && e.Currency == "USD"));
    }
}
