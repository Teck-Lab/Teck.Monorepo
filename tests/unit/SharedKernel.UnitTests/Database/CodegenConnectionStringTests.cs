using Microsoft.Extensions.Configuration;
using SharedKernel.Infrastructure.Database;
using Xunit;

namespace SharedKernel.UnitTests.Database;

public sealed class CodegenConnectionStringTests
{
    [Fact]
    public void ResolveRequired_ReturnsFirstConfiguredConnectionString()
    {
        var config = BuildConfig(
            ("ConnectionStrings:OrderWrite", "Host=db;Database=order"),
            ("ConnectionStrings:Default", "Host=db;Database=fallback"));

        string result = CodegenConnectionString.ResolveRequired(config, "OrderWrite", "Default");

        Assert.Equal("Host=db;Database=order", result);
    }

    [Fact]
    public void ResolveRequired_FallsThroughToLaterNameWhenEarlierIsMissing()
    {
        var config = BuildConfig(("ConnectionStrings:Default", "Host=db;Database=fallback"));

        string result = CodegenConnectionString.ResolveRequired(config, "OrderWrite", "Default");

        Assert.Equal("Host=db;Database=fallback", result);
    }

    [Fact]
    public void ResolveRequired_ThrowsWhenNoneConfiguredOutsideCodeGeneration()
    {
        var config = BuildConfig();

        // The unit-test process is not started with the `codegen` command, so the resolver
        // enforces a real connection string instead of returning the build-time placeholder.
        Assert.Throws<InvalidOperationException>(
            () => CodegenConnectionString.ResolveRequired(config, "OrderWrite", "Default"));
    }

    [Fact]
    public void ResolveRequired_ThrowsArgumentNullForNullConfiguration()
    {
        Assert.Throws<ArgumentNullException>(
            () => CodegenConnectionString.ResolveRequired(null!, "OrderWrite"));
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
