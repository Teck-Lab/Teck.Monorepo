namespace Gateway.Public.Edge;

/// <summary>Builds and holds the route-id to <see cref="EdgeAccessPolicy"/> map, validated at startup.</summary>
public sealed class EdgeAccessPolicyRegistry : IEdgeAccessPolicyRegistry
{
    private readonly IReadOnlyDictionary<string, EdgeAccessPolicy> policies;

    private EdgeAccessPolicyRegistry(IReadOnlyDictionary<string, EdgeAccessPolicy> policies) => this.policies = policies;

    /// <summary>Binds every route's edge policy from configuration; throws if a non-anonymous route lacks an audience.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The validated registry.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a non-anonymous route has no resolvable exchange audience.</exception>
    public static EdgeAccessPolicyRegistry Build(IConfiguration configuration)
    {
        var map = new Dictionary<string, EdgeAccessPolicy>(StringComparer.OrdinalIgnoreCase);

        foreach (IConfigurationSection route in configuration.GetSection("ReverseProxy:Routes").GetChildren())
        {
            string routeId = route.Key;
            string modeText = route["Metadata:EdgeAccess"] ?? nameof(EdgeAccessMode.Authenticated);
            EdgeAccessMode mode = Enum.Parse<EdgeAccessMode>(modeText, ignoreCase: true);

            string? audience = null;
            if (mode != EdgeAccessMode.Anonymous)
            {
                string? clusterId = route["ClusterId"];
                audience = ResolveAudience(configuration, clusterId);
                if (string.IsNullOrWhiteSpace(audience))
                {
                    throw new InvalidOperationException(
                        $"Route '{routeId}' is '{mode}' but has no exchange audience " +
                        $"(set Clusters:{clusterId}:Destinations:*:AccessTokenClientName).");
                }

                string? authzPolicy = route["AuthorizationPolicy"];
                if (!string.Equals(authzPolicy, "authenticated", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Route '{routeId}' is '{mode}' but its AuthorizationPolicy is " +
                        $"'{authzPolicy ?? "(null)"}' — set AuthorizationPolicy to 'authenticated'.");
                }
            }

            map[routeId] = new EdgeAccessPolicy(mode, audience);
        }

        return new EdgeAccessPolicyRegistry(map);
    }

    /// <inheritdoc/>
    public EdgeAccessPolicy? ForRoute(string routeId) =>
        policies.TryGetValue(routeId, out EdgeAccessPolicy? policy) ? policy : null;

    private static string? ResolveAudience(IConfiguration configuration, string? clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
        {
            return null;
        }

        foreach (IConfigurationSection destination in
                 configuration.GetSection($"ReverseProxy:Clusters:{clusterId}:Destinations").GetChildren())
        {
            string? name = destination["AccessTokenClientName"];
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        return null;
    }
}
