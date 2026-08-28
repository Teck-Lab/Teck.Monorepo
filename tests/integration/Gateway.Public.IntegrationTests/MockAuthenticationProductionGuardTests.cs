using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Gateway.Public.IntegrationTests;

/// <summary>Regression coverage for the production-only test-authentication startup guard.</summary>
public sealed class MockAuthenticationProductionGuardTests
{
    /// <summary>Production startup must reject an attempt to activate the test-only authentication flag.</summary>
    [Fact]
    public void Gateway_WhenProductionMockAuthenticationIsEnabled_Throws()
    {
        using var factory = new ProductionMockAuthenticationFactory();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => _ = factory.Services);

        Assert.Equal("Mock authentication must never be enabled in Production.", exception.Message);
    }

    /// <summary>The gateway binary must never compile the test-only handler into a production-usable path.</summary>
    [Fact]
    public void GatewayAssembly_WhenTypesAreInspected_DoesNotContainMockAuthenticationHandler()
    {
        Assert.DoesNotContain(
            typeof(Program).Assembly.GetTypes(),
            type => string.Equals(type.Name, "MockBearerAuthenticationHandler", StringComparison.Ordinal));
    }

    private sealed class ProductionMockAuthenticationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Testing:UseMockAuthentication", "true");
        }
    }
}
