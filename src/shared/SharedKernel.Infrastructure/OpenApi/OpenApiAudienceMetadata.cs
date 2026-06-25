namespace SharedKernel.Infrastructure.OpenApi;

/// <summary>
/// Marks endpoint visibility audience for OpenAPI document filtering.
/// </summary>
/// <param name="Audiences">The target audiences (for example, web, mobile, admin).</param>
public sealed record OpenApiAudienceMetadata(params string[] Audiences)
{
    /// <summary>
    /// Gets normalized non-empty audience values.
    /// </summary>
    /// <returns>A normalized, distinct audience list.</returns>
    public IReadOnlyCollection<string> GetNormalizedAudiences()
    {
        return Audiences
            .SelectMany(static value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
