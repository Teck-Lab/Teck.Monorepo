using SharedKernel.Events;

namespace Inventories.Application.Inventory.Features.CommitReservation.V1;

/// <summary>
/// The result of a <see cref="ReservationCommitter"/> commit, carrying everything the caller needs
/// to publish the appropriate integration events after the commit (or non-commit) has been decided.
/// </summary>
/// <param name="Outcome">What the committer decided.</param>
/// <param name="ReservationId">The committed reservation identifier, or <see cref="System.Guid.Empty"/> when nothing was committed.</param>
/// <param name="Lines">The reserved lines (on <see cref="ReservationCommitOutcome.Committed"/>) or the failing lines (on <see cref="ReservationCommitOutcome.Rejected"/>/<see cref="ReservationCommitOutcome.Contention"/>).</param>
/// <param name="AffectedStock">The stock items touched by a successful commit; empty otherwise.</param>
/// <param name="RequiresFreshScopeRejectionHandling">Whether rejection follow-up must avoid the failed ambient unit of work.</param>
internal sealed record ReservationCommitResult(
    ReservationCommitOutcome Outcome,
    Guid ReservationId,
    IReadOnlyList<StockReservationLine> Lines,
    IReadOnlyList<AffectedStock> AffectedStock,
    bool RequiresFreshScopeRejectionHandling = false)
{
    /// <summary>Creates a result for an idempotent no-op (a reservation for the source already existed).</summary>
    /// <param name="reservationId">The identifier of the pre-existing reservation.</param>
    /// <returns>The result.</returns>
    public static ReservationCommitResult AlreadyReserved(Guid reservationId) =>
        new(ReservationCommitOutcome.AlreadyReserved, reservationId, [], []);

    /// <summary>Creates a result for a version-two handoff to an already-committed legacy order reservation.</summary>
    /// <param name="reservationId">The existing reservation identifier.</param>
    /// <param name="lines">The persisted reservation lines to include in the V2 outcome.</param>
    /// <returns>The handoff result.</returns>
    public static ReservationCommitResult LifecycleHandoff(Guid reservationId, IReadOnlyList<StockReservationLine> lines) =>
        new(ReservationCommitOutcome.LifecycleHandoff, reservationId, lines, []);

    /// <summary>Creates a result for a successful commit.</summary>
    /// <param name="reservationId">The committed reservation identifier.</param>
    /// <param name="lines">The reserved lines.</param>
    /// <param name="affectedStock">The stock items touched by the commit.</param>
    /// <returns>The result.</returns>
    public static ReservationCommitResult Committed(
        Guid reservationId,
        IReadOnlyList<StockReservationLine> lines,
        IReadOnlyList<AffectedStock> affectedStock) =>
        new(ReservationCommitOutcome.Committed, reservationId, lines, affectedStock);

    /// <summary>Creates a result for an all-or-nothing rejection.</summary>
    /// <param name="failingLines">The lines that could not be satisfied.</param>
    /// <returns>The result.</returns>
    public static ReservationCommitResult Rejected(IReadOnlyList<StockReservationLine> failingLines) =>
        new(ReservationCommitOutcome.Rejected, Guid.Empty, failingLines, []);

    /// <summary>Creates a result for exhausted optimistic-concurrency retries.</summary>
    /// <param name="lines">The requested lines, reported as contended.</param>
    /// <returns>The result.</returns>
    public static ReservationCommitResult Contention(IReadOnlyList<StockReservationLine> lines) =>
        new(ReservationCommitOutcome.Contention, Guid.Empty, lines, []);
}
