namespace SharedKernel.Infrastructure.OpenApi;

/// <summary>
/// Extension to hold a list of openapi documents.
/// </summary>
public static class OpenApiDocumentRegistry
{
    private static readonly List<(string Name, string Url)> _documents = new();

    /// <summary>
    /// Add document to list.
    /// </summary>
    /// <param name="name">The name of the OpenAPI document.</param>
    /// <param name="url">The URL of the OpenAPI document.</param>
    public static void Add(string name, string url) =>
        _documents.Add((name, url));

    /// <summary>
    /// Add document to list using a Uri.
    /// </summary>
    /// <param name="name">The name of the OpenAPI document.</param>
    /// <param name="url">The Uri of the OpenAPI document.</param>
    public static void Add(string name, Uri url) =>
        _documents.Add((name, url.ToString()));

    /// <summary>
    /// ReadOnly list of openapi documents.
    /// </summary>
    /// <returns></returns>
    public static IReadOnlyList<(string Name, string Url)> GetAll() =>
        _documents.AsReadOnly();
}
