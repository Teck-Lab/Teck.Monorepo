using Gateway.Public.Edge;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gateway.Public.UnitTests.Edge;

/// <summary>Unit tests for <see cref="EdgeAccessPolicyRegistry"/>.</summary>
public sealed class EdgeAccessPolicyRegistryTests
{
    private static IConfiguration Config(string routeMode, bool withAudience, string? authzPolicy = "authenticated")
    {
        var dict = new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:r1:ClusterId"] = "order",
            ["ReverseProxy:Routes:r1:Metadata:EdgeAccess"] = routeMode,
        };
        if (withAudience)
        {
            dict["ReverseProxy:Clusters:order:Destinations:primary:AccessTokenClientName"] = "order";
        }

        if (authzPolicy is not null)
        {
            dict["ReverseProxy:Routes:r1:AuthorizationPolicy"] = authzPolicy;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    /// <summary>Build should bind the exchange audience from the cluster destination metadata.</summary>
    [Fact]
    public void Build_BindsAudience_FromClusterDestination()
    {
        var registry = EdgeAccessPolicyRegistry.Build(Config("Authenticated", withAudience: true));
        var policy = registry.ForRoute("r1");
        Assert.NotNull(policy);
        Assert.Equal(EdgeAccessMode.Authenticated, policy!.Mode);
        Assert.Equal("order", policy.ExchangeAudience);
    }

    /// <summary>Build should throw when a non-anonymous route has no resolvable audience.</summary>
    [Fact]
    public void Build_Throws_WhenNonAnonymousRouteHasNoAudience()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EdgeAccessPolicyRegistry.Build(Config("Authenticated", withAudience: false)));
        Assert.Contains("r1", ex.Message);
    }

    /// <summary>Build should allow an anonymous route without any audience configured.</summary>
    [Fact]
    public void Build_AllowsAnonymousRoute_WithoutAudience()
    {
        var registry = EdgeAccessPolicyRegistry.Build(Config("Anonymous", withAudience: false, authzPolicy: null));
        Assert.Equal(EdgeAccessMode.Anonymous, registry.ForRoute("r1")!.Mode);
    }

    /// <summary>Build should throw when a non-anonymous route is missing AuthorizationPolicy == "authenticated".</summary>
    [Fact]
    public void Build_Throws_WhenNonAnonymousRouteMissingAuthorizationPolicy()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EdgeAccessPolicyRegistry.Build(Config("Authenticated", withAudience: true, authzPolicy: null)));
        Assert.Contains("r1", ex.Message);
        Assert.Contains("AuthorizationPolicy", ex.Message);
    }

    /// <summary>Build should throw when a non-anonymous route has AuthorizationPolicy set to a value other than "authenticated".</summary>
    [Fact]
    public void Build_Throws_WhenNonAnonymousRouteHasWrongAuthorizationPolicy()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EdgeAccessPolicyRegistry.Build(Config("Authenticated", withAudience: true, authzPolicy: "allow-anonymous")));
        Assert.Contains("r1", ex.Message);
        Assert.Contains("AuthorizationPolicy", ex.Message);
    }
}
