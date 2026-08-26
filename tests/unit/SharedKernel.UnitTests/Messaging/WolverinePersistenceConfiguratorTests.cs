using JasperFx;
using JasperFx.CodeGeneration;
using SharedKernel.Infrastructure.Messaging;
using Wolverine;
using Wolverine.RabbitMQ;
using Wolverine.Transports;
using Xunit;

namespace SharedKernel.UnitTests.Messaging;

public sealed class WolverinePersistenceConfiguratorTests
{
    private const string DummyWriteConnectionString = "Host=localhost;Database=x;Username=x;Password=x";

    [Fact]
    public void ConfigureLocalOnlyRuntime_SetsAutoBuildMessageStorageOnStartupToCreateOrUpdate_WhenDevelopment()
    {
        var options = new WolverineOptions();

        WolverinePersistenceConfigurator.ConfigureLocalOnlyRuntime(options, isDevelopment: true, DummyWriteConnectionString);

        Assert.Equal(AutoCreate.CreateOrUpdate, options.AutoBuildMessageStorageOnStartup);
    }

    [Fact]
    public void ConfigureLocalOnlyRuntime_SetsAutoBuildMessageStorageOnStartupToCreateOrUpdate_WhenNotDevelopment()
    {
        var options = new WolverineOptions();

        WolverinePersistenceConfigurator.ConfigureLocalOnlyRuntime(options, isDevelopment: false, DummyWriteConnectionString);

        // Regression guard: AutoBuildMessageStorageOnStartup must stay unconditional (CreateOrUpdate in every
        // environment). It was previously gated on `isDevelopment`, which left production with no way to create
        // the `wolverine` message-store schema. If this assertion ever fails, someone reintroduced that gate.
        Assert.Equal(AutoCreate.CreateOrUpdate, options.AutoBuildMessageStorageOnStartup);
    }

    [Fact]
    public void ConfigureLocalOnlyRuntime_KeepsTypeLoadModeGatedByEnvironment()
    {
        var developmentOptions = new WolverineOptions();
        var productionOptions = new WolverineOptions();

        WolverinePersistenceConfigurator.ConfigureLocalOnlyRuntime(developmentOptions, isDevelopment: true, DummyWriteConnectionString);
        WolverinePersistenceConfigurator.ConfigureLocalOnlyRuntime(productionOptions, isDevelopment: false, DummyWriteConnectionString);

        // Only AutoBuildMessageStorageOnStartup was made unconditional; TypeLoadMode must still differ by
        // environment (Dynamic in dev for runtime codegen, Static in prod for the pre-generated Docker build).
        Assert.Equal(TypeLoadMode.Dynamic, developmentOptions.CodeGeneration.TypeLoadMode);
        Assert.Equal(TypeLoadMode.Static, productionOptions.CodeGeneration.TypeLoadMode);
    }

    [Fact]
    public void ConfigureStandardRuntime_PreservesMessageTypeListenerNamingByDefault()
    {
        var options = new WolverineOptions();

        WolverinePersistenceConfigurator.ConfigureStandardRuntime(
            options,
            isDevelopment: true,
            DummyWriteConnectionString,
            "amqp://guest:guest@localhost:5672");

        Assert.Equal(NamingSource.FromMessageType, ListenerNamingSource(options));
    }

    [Fact]
    public void ConfigureStandardRuntime_CanUseHandlerTypeListenerNamingForIndependentConsumers()
    {
        var options = new WolverineOptions();

        WolverinePersistenceConfigurator.ConfigureStandardRuntime(
            options,
            isDevelopment: true,
            DummyWriteConnectionString,
            "amqp://guest:guest@localhost:5672",
            listenerNamingSource: NamingSource.FromHandlerType);

        Assert.Equal(NamingSource.FromHandlerType, ListenerNamingSource(options));
    }

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

    private static NamingSource ListenerNamingSource(WolverineOptions options)
    {
        var property = typeof(WolverineOptions).GetProperty(
            "RoutingConventions",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var conventions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(property?.GetValue(options));
        var convention = Assert.IsType<RabbitMqMessageRoutingConvention>(Assert.Single(conventions.Cast<object>()));
        var field = convention.GetType().BaseType!.GetField("_namingSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return Assert.IsType<NamingSource>(field?.GetValue(convention));
    }
}
