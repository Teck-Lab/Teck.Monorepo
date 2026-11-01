namespace Licensing.Application;

/// <summary>Signed policy together with its persisted lifecycle status.</summary>
/// <param name="Policy">The immutable signed policy document.</param>
/// <param name="Status">The policy lifecycle status.</param>
public sealed record EnterprisePolicyState(EnterprisePolicyDocument Policy, EnterprisePolicyStatus Status);
