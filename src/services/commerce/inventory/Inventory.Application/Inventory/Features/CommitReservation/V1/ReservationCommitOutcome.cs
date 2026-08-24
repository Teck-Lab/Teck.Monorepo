namespace Inventories.Application.Inventory.Features.CommitReservation.V1;

/// <summary>The outcome of a <see cref="ReservationCommitter"/> commit attempt.</summary>
internal enum ReservationCommitOutcome
{
    /// <summary>A reservation for this source already existed; nothing was mutated or published.</summary>
    AlreadyReserved,

    /// <summary>A legacy order reservation was atomically associated with a version-two lifecycle event.</summary>
    LifecycleHandoff,

    /// <summary>Stock was reserved and the reservation was committed in a single save.</summary>
    Committed,

    /// <summary>At least one line could not be satisfied; nothing was mutated (all-or-nothing).</summary>
    Rejected,

    /// <summary>Persistent optimistic-concurrency contention exhausted the retry budget without committing.</summary>
    Contention,
}
