using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ServiceDiscovery;
using Teck.ServiceDefaults;
using Xunit;

namespace Teck.ServiceDefaults.UnitTests;

public sealed class AddServiceDefaultsTests
{
    [Fact]
    public void AddServiceDefaults_RegistersServiceDiscovery()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;

        // BindValidateReturn<SerilogOptions> throws ConfigurationMissingException when the
        // "SerilogOptions" section is completely absent. Provide one key so the section exists
        // and Get<SerilogOptions>() returns an instance (other fields use their defaults).
        builder.Configuration["SerilogOptions:EnableConsole"] = "false";

        builder.AddServiceDefaults();
        using var app = builder.Build();

        // Service discovery registers ServiceEndpointResolver in DI.
        Assert.NotNull(app.Services.GetService<ServiceEndpointResolver>());
    }

    [Fact]
    public void AddServiceDefaults_ComposesObservabilityAndResilience()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty;

        // BindValidateReturn<SerilogOptions> throws ConfigurationMissingException when the
        // "SerilogOptions" section is completely absent. Provide one key so the section exists
        // and Get<SerilogOptions>() returns an instance (other fields use their defaults).
        builder.Configuration["SerilogOptions:EnableConsole"] = "false";

        builder.AddServiceDefaults();
        using var app = builder.Build();

        // Serilog logger is registered by AddTeckCloudObservability.
        Assert.NotNull(app.Services.GetService<Serilog.ILogger>());
        // IHttpClientFactory exists because ConfigureHttpClientDefaults was called.
        Assert.NotNull(app.Services.GetService<IHttpClientFactory>());
    }
}
