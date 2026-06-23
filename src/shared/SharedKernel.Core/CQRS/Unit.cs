namespace SharedKernel.Core.CQRS;

/// <summary>
/// Represents a void result. Replacement for Mediator's Unit type.
/// Used by ICommand (non-generic) to indicate no response value.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    public static readonly Unit Value = default;
    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public static bool operator ==(Unit left, Unit right) => true;
    public static bool operator !=(Unit left, Unit right) => false;
    public override string ToString() => "Unit";
}
