using Microsoft.Extensions.Configuration;
using SharedKernel.Infrastructure.Database;
using SharedKernel.Infrastructure.Hosting;
using Xunit;

namespace SharedKernel.UnitTests.Hosting;

public sealed class TeckMessagingExtensionsTests
{
    [Fact]
    public void ShouldUseBroker_ReturnsTrueWhenRabbitConnectionStringIsPresent()
    {
        bool result = TeckMessagingExtensions.ShouldUseBroker("rabbitmq://u:p@host:5672");

        Assert.True(result);
    }

    [Fact]
    public void ShouldUseBroker_ReturnsFalseWhenRabbitConnectionStringIsNull()
    {
        bool result = TeckMessagingExtensions.ShouldUseBroker(null);

        Assert.False(result);
    }

    [Fact]
    public void ShouldUseBroker_ReturnsFalseWhenRabbitConnectionStringIsWhitespace()
    {
        bool result = TeckMessagingExtensions.ShouldUseBroker("   ");

        Assert.False(result);
    }

    [Fact]
    public void ShouldUseBroker_ReflectsConfiguredRabbitmqConnectionString()
    {
        IConfiguration configWithBroker = BuildConfig(("ConnectionStrings:rabbitmq", "rabbitmq://u:p@host:5672"));
        IConfiguration configWithoutBroker = BuildConfig(("ConnectionStrings:Default", "Host=db;Database=order"));

        Assert.True(TeckMessagingExtensions.ShouldUseBroker(configWithBroker.GetConnectionString("rabbitmq")));
        Assert.False(TeckMessagingExtensions.ShouldUseBroker(configWithoutBroker.GetConnectionString("rabbitmq")));
    }

    [Fact]
    public void ResolveRequired_ResolvesWriteConnectionStringIndependentlyOfBrokerPresence()
    {
        IConfiguration config = BuildConfig(
            ("ConnectionStrings:OrderWrite", "Host=db;Database=order"),
            ("ConnectionStrings:rabbitmq", "rabbitmq://u:p@host:5672"));

        string write = CodegenConnectionString.ResolveRequired(config, "OrderWrite", "Default");

        Assert.Equal("Host=db;Database=order", write);
    }

    private static IConfiguration BuildConfig(params (string Key, string Value)[] entries)
    {
        var values = new Dictionary<string, string?>();
        foreach ((string key, string value) in entries)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
