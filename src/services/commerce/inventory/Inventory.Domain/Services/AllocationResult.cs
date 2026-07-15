using Inventories.Domain.ValueObjects;

namespace Inventories.Domain.Services;

/// <summary>
/// Represents the outcome of a <see cref="StockAllocator"/> allocation attempt: whether the
/// requested quantity could be covered, the per-location allocations drawn from available stock,
/// and any portion absorbed as a backorder.
/// </summary>
/// <param name="Satisfied">
/// Whether the requested quantity was fully covered, either entirely from available stock or with
/// the shortfall absorbed as a backorder.
/// </param>
/// <param name="Allocations">The per-location quantities drawn from available stock, in priority order.</param>
/// <param name="BackorderedQuantity">The portion of the requested quantity absorbed as a backorder rather than drawn from available stock.</param>
public sealed record AllocationResult(bool Satisfied, IReadOnlyList<Allocation> Allocations, int BackorderedQuantity);
