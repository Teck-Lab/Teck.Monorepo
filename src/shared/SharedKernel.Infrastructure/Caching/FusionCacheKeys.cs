namespace SharedKernel.Infrastructure.Caching
{
    /// <summary>
    /// Builds consistent FusionCache keys for infrastructure-level concerns.
    /// </summary>
    internal static class FusionCacheKeys
    {
        private const string Prefix = "fc";
        private const string Version = "v1";

        public static string TenantByIdentifier(string identifier) =>
            Build("tenant", "by-identifier", identifier);

        public static string TenantById(string tenantId) =>
            Build("tenant", "by-id", tenantId);

        public static string TenantByName(string name) =>
            Build("tenant", "by-name", name);

        public static string AllTenantsPage(string strategy, int size, int page) =>
            Build("tenant", "all", "strategy", strategy, "size", size.ToString(), "page", page.ToString());

        public static string PrimaryTenantIdForSet(IEnumerable<string> tenantIds)
        {
            var normalized = tenantIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Build("tenant", "primary-id", string.Join(",", normalized));
        }

        private static string Build(params string[] segments)
        {
            var encoded = segments.Select(Uri.EscapeDataString);
            return $"{Prefix}:{Version}:{string.Join(':', encoded)}";
        }
    }
}
