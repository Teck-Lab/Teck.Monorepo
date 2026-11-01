namespace Licensing.Application;

/// <summary>Lifecycle status of an exact immutable enterprise policy version.</summary>
public enum EnterprisePolicyStatus
{
    /// <summary>The policy is valid and is not revoked or superseded.</summary>
    Active,

    /// <summary>An operator has revoked this policy.</summary>
    Revoked,

    /// <summary>A later issuance for the same purchase supersedes this policy.</summary>
    Superseded,

    /// <summary>The policy validity start has not been reached.</summary>
    NotYetValid,

    /// <summary>The policy validity end has been reached.</summary>
    Expired,
}
