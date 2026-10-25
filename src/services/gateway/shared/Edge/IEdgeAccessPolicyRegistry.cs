namespace Gateway.Edge;

/// <summary>Resolves the <see cref="EdgeAccessPolicy"/> for a route id.</summary>
public interface IEdgeAccessPolicyRegistry
{
    /// <summary>Gets the validated policy for the given YARP route id, rejecting unknown routes.</summary>
    /// <param name="routeId">The YARP route id.</param>
    /// <returns>The validated policy.</returns>
    EdgeAccessPolicy ForRoute(string routeId);
}
