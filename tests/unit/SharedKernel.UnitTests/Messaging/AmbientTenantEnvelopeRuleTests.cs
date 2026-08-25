using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Infrastructure.Messaging;
using SharedKernel.Infrastructure.Messaging.MultiTenant;
using SharedKernel.Infrastructure.MultiTenant;
using Wolverine;
using Xunit;

namespace SharedKernel.UnitTests.Messaging;

public sealed class AmbientTenantEnvelopeRuleTests
{
    private const string TenantId = "00000000-0000-0000-0000-000000000001";

    [Fact]
    public async Task ConfigureLocalOnlyRuntime_HttpOriginatedTenantContext_StampsOutgoingEnvelope()
    {
        WolverineOptions options = new();
        await using (options.ConfigureAwait(false))
        {
            WolverinePersistenceConfigurator.ConfigureLocalOnlyRuntime(options, isDevelopment: true, "Host=localhost;Database=x;Username=x;Password=x");

            var accessor = Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();
            accessor.MultiTenantContext.Returns(new MultiTenantContext<TenantDetails>(new TenantDetails
            {
                Id = TenantId,
                Identifier = TenantId,
                Name = "Tenant A",
                IsActive = true,
            }));

            var middleware = new TenantPropagationMiddleware(
                accessor,
                Substitute.For<IMultiTenantContextSetter>(),
                Substitute.For<ILogger<TenantPropagationMiddleware>>());
            var messageContext = Substitute.For<IMessageContext>();
            messageContext.Envelope.Returns((Envelope?)null);

            TenantPropagationMiddleware.TenantPropagationScope scope = middleware.Before(messageContext);
            try
            {
                var outgoing = new Envelope(new TestIntegrationEvent());
                var rule = Assert.Single(options.MetadataRules.OfType<AmbientTenantEnvelopeRule>());

                rule.Modify(outgoing);

                Assert.Equal(TenantId, outgoing.TenantId);
                Assert.Equal(TenantId, outgoing.Headers[AmbientTenantEnvelopeRule.TenantHeaderName]);
            }
            finally
            {
                middleware.Finally(scope);
            }
        }
    }

    [Fact]
    public void Modify_NoResolvedTenant_LeavesOutgoingEnvelopeTenantless()
    {
        TenantPropagationContext.CurrentTenantId = null;
        var outgoing = new Envelope(new TestIntegrationEvent());

        new AmbientTenantEnvelopeRule().Modify(outgoing);

        Assert.Null(outgoing.TenantId);
        Assert.DoesNotContain(AmbientTenantEnvelopeRule.TenantHeaderName, outgoing.Headers.Keys);
    }

    [Fact]
    public void Modify_ExplicitEnvelopeTenant_PreservesExistingTenant()
    {
        TenantPropagationContext.CurrentTenantId = TenantId;
        try
        {
            var outgoing = new Envelope(new TestIntegrationEvent()) { TenantId = "explicit-tenant" };

            new AmbientTenantEnvelopeRule().Modify(outgoing);

            Assert.Equal("explicit-tenant", outgoing.TenantId);
            Assert.Equal("explicit-tenant", outgoing.Headers[AmbientTenantEnvelopeRule.TenantHeaderName]);
        }
        finally
        {
            TenantPropagationContext.CurrentTenantId = null;
        }
    }

    private sealed record TestIntegrationEvent;
}
