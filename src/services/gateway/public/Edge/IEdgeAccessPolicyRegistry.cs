namespace Gateway.Public.Edge;

/// <summary>Resolves the <see cref="EdgeAccessPolicy"/> for a route id.</summary>
public interface IEdgeAccessPolicyRegistry
{
    /// <summary>Gets the policy for the given YARP route id, or <see langword="null"/> if unknown.</summary>
    /// <param name="routeId">The YARP route id.</param>
    /// <returns>The policy or <see langword="null"/>.</returns>
    EdgeAccessPolicy? ForRoute(string routeId);
}
