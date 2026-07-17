using SharedKernel.Infrastructure.Messaging;
using Xunit;

namespace SharedKernel.UnitTests.Messaging;

public sealed class WolverinePersistenceConfiguratorTests
{
    [Fact]
    public void NormalizeRabbitConnectionString_ConvertsRabbitMqSchemeToAmqp()
    {
        string result = WolverinePersistenceConfigurator.NormalizeRabbitConnectionString("rabbitmq://u:p@host:5672");

        Assert.Equal("amqp://u:p@host:5672", result);
    }

    [Fact]
    public void NormalizeRabbitConnectionString_ConvertsRabbitMqsSchemeToAmqps()
    {
        string result = WolverinePersistenceConfigurator.NormalizeRabbitConnectionString("rabbitmqs://u:p@host:5671");

        Assert.Equal("amqps://u:p@host:5671", result);
    }

    [Fact]
    public void NormalizeRabbitConnectionString_PassesThroughAlreadyNormalizedAmqpString()
    {
        string result = WolverinePersistenceConfigurator.NormalizeRabbitConnectionString("amqp://u:p@host:5672");

        Assert.Equal("amqp://u:p@host:5672", result);
    }

    [Fact]
    public void NormalizeRabbitConnectionString_PassesThroughAlreadyNormalizedAmqpsString()
    {
        string result = WolverinePersistenceConfigurator.NormalizeRabbitConnectionString("amqps://u:p@host:5671");

        Assert.Equal("amqps://u:p@host:5671", result);
    }

    [Fact]
    public void NormalizeRabbitConnectionString_TrimsWhitespaceForUnrecognizedScheme()
    {
        string result = WolverinePersistenceConfigurator.NormalizeRabbitConnectionString("  amqp://host  ");

        Assert.Equal("amqp://host", result);
    }

    [Fact]
    public void NormalizeRabbitConnectionString_IsCaseInsensitiveForScheme()
    {
        string result = WolverinePersistenceConfigurator.NormalizeRabbitConnectionString("RabbitMQ://u:p@host:5672");

        Assert.Equal("amqp://u:p@host:5672", result);
    }
}
