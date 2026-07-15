namespace Inventories.Application.Inventory.Features.CommitReservation.V1;

/// <summary>
/// A post-commit snapshot of a stock item touched by a commit, carrying the flags the caller needs
/// to decide which stock-level integration events to publish. Captured as plain values before the
/// per-attempt scope is disposed, so it is safe to read after the DbContext is gone.
/// </summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="LocationId">The location identifier.</param>
/// <param name="TenantId">The owning tenant identifier.</param>
/// <param name="Available">The available quantity after the reservation was applied.</param>
/// <param name="ReorderThreshold">The reorder threshold configured for the stock item.</param>
/// <param name="NewlyDepleted">Whether the reservation drove available quantity to depletion.</param>
/// <param name="NewlyReorderTriggered">Whether the reservation drove available quantity down across the reorder threshold.</param>
internal sealed record AffectedStock(
    Guid ProductId,
    Guid LocationId,
    string TenantId,
    int Available,
    int ReorderThreshold,
    bool NewlyDepleted,
    bool NewlyReorderTriggered);
