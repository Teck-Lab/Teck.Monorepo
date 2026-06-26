namespace Orders.Host.Endpoints.Orders;

/// <summary>
/// Request payload for retrieving a single order by its identifier.
/// </summary>
/// <param name="Id">The identifier of the order to retrieve.</param>
public sealed record GetOrderRequest(Guid Id);
