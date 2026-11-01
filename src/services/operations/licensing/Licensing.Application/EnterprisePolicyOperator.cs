namespace Licensing.Application;

/// <summary>Authenticated human operator claims presented to enterprise policy operations.</summary>
public sealed record EnterprisePolicyOperator(string Subject, string Audience, IReadOnlySet<string> Scopes, IReadOnlySet<string> Roles);
