using System.Text.Json;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SharedKernel.Infrastructure.MultiTenant;

/// <summary>
/// Strategy to use when multiple tenant IDs are available.
/// </summary>
public enum MultiTenantResolutionStrategy
{
    /// <summary>
    /// Use the first tenant ID in the list.
    /// </summary>
    First,

    /// <summary>
    /// Use the primary tenant ID (when the primary tenant is indicated).
    /// </summary>
    Primary,

    /// <summary>
    /// Use the tenant ID from the request context (URL, header, etc.)
    /// </summary>
    FromRequest,

    /// <summary>
    /// Let the application code handle the resolution.
    /// </summary>
    Custom,
}

/// <summary>
/// Extension methods for configuring multi-tenant functionality in Teck.Cloud applications.
/// </summary>
public static class MultiTenantExtensions
{
    /// <summary>
    /// Adds comprehensive tenant resolution strategies to a service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for multi-tenant strategies.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTeckCloudMultiTenancy(
        this IServiceCollection services,
        Action<TeckCloudMultiTenancyOptions>? configureOptions = null)
    {
        // Create and configure options
        var options = new TeckCloudMultiTenancyOptions();
        configureOptions?.Invoke(options);

        var builder = services.AddMultiTenant<TenantDetails>();

        // Configure strategies based on options
        if (options.UseClaimStrategy)
        {
            builder.WithDelegateStrategy(ResolveClaimStrategy);
        }

        if (options.UseHeaderStrategy)
        {
            builder.WithDelegateStrategy(ResolveHeaderStrategy);
        }

        // Configure store
        if (options.UseCustomerApiTenantStore)
        {
            // Configure options
            services.Configure<TeckCloudMultiTenancyOptions>(option =>
            {
                option.UseCustomerApiTenantStore = options.UseCustomerApiTenantStore;
                option.UseDistributedCacheWithCustomerApi = options.UseDistributedCacheWithCustomerApi;
                option.CustomerApiOptions = options.CustomerApiOptions;
            });

            if (!options.UseDistributedCacheWithCustomerApi)
            {
                // Also ensure in-memory cache is available
                services.AddMemoryCache();
            }

            // Register the CustomerApiTenantStore with Finbuckle
            services.AddScoped<IMultiTenantStore<TenantDetails>, CustomerApiTenantStore>();
            builder.WithStore<CustomerApiTenantStore>(ServiceLifetime.Scoped);
        }
        else
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IMultiTenantStore<TenantDetails>, HeaderTenantStore>();
            builder.WithStore<HeaderTenantStore>(ServiceLifetime.Scoped);
        }

        return services;
    }

    /// <summary>
    /// Configures HTTP client for tenant resolution and adds it to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tenantApiUrl">The base URL for the tenant API.</param>
    /// <param name="httpClientName">The name for the HTTP client.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTenantHttpClient(
this IServiceCollection services,
Uri tenantApiUrl,
string httpClientName = "TenantApi")
    {
        services.AddHttpClient(httpClientName, client =>
        {
            client.BaseAddress = tenantApiUrl;
        });

        return services;
    }

    // Helper method to resolve tenant ID from claims
    private static async Task<string?> ResolveClaimStrategy(object context)
    {
        var httpContext = context as HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // Get options to determine which claim names to use
        var options = httpContext.RequestServices.GetService<IOptions<TeckCloudMultiTenancyOptions>>()?.Value
            ?? new TeckCloudMultiTenancyOptions();

        // First check for the organization claim (new nested JSON structure)
        var organizationClaim = httpContext.User.FindFirst(options.OrganizationClaimName);
        if (organizationClaim != null && !string.IsNullOrWhiteSpace(organizationClaim.Value))
        {
            try
            {
                // Parse the JSON from the claim
                // Expected format: { "OrgName1": { "id": "guid1" }, "OrgName2": { "id": "guid2" } }
                var organizationsJson = JsonDocument.Parse(organizationClaim.Value);
                var tenantIds = new List<string>();
                var tenantNames = new Dictionary<string, string>(); // Map of tenant ID to tenant name

                // Extract organization IDs from the JSON structure
                foreach (var org in organizationsJson.RootElement.EnumerateObject())
                {
                    string tenantName = org.Name; // This is the tenant name (e.g., "Dagrofa")

                    if (org.Value.TryGetProperty("id", out var idProperty) &&
                        idProperty.ValueKind == JsonValueKind.String)
                    {
                        var orgId = idProperty.GetString();
                        if (!string.IsNullOrEmpty(orgId))
                        {
                            tenantIds.Add(orgId);
                            tenantNames[orgId] = tenantName;
                        }
                    }
                }

                if (tenantIds.Count > 0)
                {
                    // Store all tenant IDs and names in context for potential later use
                    httpContext.Items["AvailableTenantIds"] = tenantIds.ToArray();
                    httpContext.Items["TenantNames"] = tenantNames;

                    // Process according to the strategy
                    return await ResolveTenantIdFromList(httpContext, tenantIds.ToArray(), options, context);
                }
            }
            catch (JsonException exception)
            {
                // If JSON parsing fails, log and fall back to other strategies
                var logger = httpContext.RequestServices.GetService<ILogger<IMultiTenantContext>>();
                logger?.LogWarning(exception, "Failed to parse organization claim JSON: {ClaimValue}", organizationClaim.Value);
            }
        }

        // If organization claim approach fails, check for the single tenant ID claim
        var tenantClaim = httpContext.User.FindFirst(options.TenantIdClaimName);
        if (tenantClaim != null && !string.IsNullOrWhiteSpace(tenantClaim.Value))
        {
            return tenantClaim.Value;
        }

        // If not found, check for the multi-tenant claim
        var multiTenantClaim = httpContext.User.FindFirst(options.MultiTenantClaimName);
        if (multiTenantClaim != null && !string.IsNullOrWhiteSpace(multiTenantClaim.Value))
        {
            // Split the value by the separator
            var tenantIds = multiTenantClaim.Value.Split(
                new[] { options.TenantIdSeparator },
                StringSplitOptions.RemoveEmptyEntries);

            if (tenantIds.Length > 0)
            {
                // Store all tenant IDs in context for potential later use
                httpContext.Items["AvailableTenantIds"] = tenantIds;

                // Process according to the strategy
                return await ResolveTenantIdFromList(httpContext, tenantIds, options, context);
            }
        }

        return null;
    }

    // Helper method to resolve tenant ID from a list based on strategy
    private static async Task<string?> ResolveTenantIdFromList(
        HttpContext httpContext,
        string[] tenantIds,
        TeckCloudMultiTenancyOptions options,
        object context)
    {
        // If there's a tenant name specified in the request header, try to use that first
        if (options.UseHeaderStrategy &&
            httpContext.Request.Headers.TryGetValue(options.TenantNameHeaderName, out var requestedTenantName) &&
            !string.IsNullOrWhiteSpace(requestedTenantName) &&
            httpContext.Items.TryGetValue("TenantNames", out var tenantNamesObj) &&
            tenantNamesObj is Dictionary<string, string> tenantNames)
        {
            // Find the tenant ID that matches the requested name using LINQ
            var matchingTenantId = tenantNames
                .Where(kvp => string.Equals(kvp.Value, requestedTenantName.ToString(), StringComparison.OrdinalIgnoreCase)
                              && tenantIds.Contains(kvp.Key))
                .Select(kvp => kvp.Key)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(matchingTenantId))
            {
                return matchingTenantId;
            }
        }

        switch (options.MultiTenantResolutionStrategy)
        {
            case MultiTenantResolutionStrategy.First:
                return tenantIds[0];

            case MultiTenantResolutionStrategy.Primary:
                // Use the CustomerApiTenantStore to find the primary tenant
                if (options.UseCustomerApiTenantStore &&
                    httpContext.RequestServices.GetService<IMultiTenantStore<TenantDetails>>() is CustomerApiTenantStore store)
                {
                    var primaryTenantId = await store.FindPrimaryTenantIdAsync(tenantIds);
                    if (!string.IsNullOrEmpty(primaryTenantId))
                    {
                        return primaryTenantId;
                    }
                }

                // Default to first if primary can't be determined
                return tenantIds[0];

            case MultiTenantResolutionStrategy.FromRequest:
                // Try to get from header or URL
                var headerTenantId = await ResolveHeaderStrategy(context);
                if (!string.IsNullOrWhiteSpace(headerTenantId) &&
                    tenantIds.Contains(headerTenantId, StringComparer.OrdinalIgnoreCase))
                {
                    return headerTenantId;
                }

                // Default to first if not found in request
                return tenantIds[0];

            case MultiTenantResolutionStrategy.Custom:
                // Application code will handle this
                return null;

            default:
                return tenantIds[0];
        }
    }

    // Helper method to resolve tenant ID from header
    private static Task<string?> ResolveHeaderStrategy(object context)
    {
        var httpContext = context as HttpContext;
        if (httpContext == null)
        {
            return Task.FromResult<string?>(null);
        }

        // Get options to determine which header name to use
        var options = httpContext.RequestServices.GetService<IOptions<TeckCloudMultiTenancyOptions>>()?.Value
            ?? new TeckCloudMultiTenancyOptions();

        var logger = httpContext.RequestServices.GetService<ILogger<IMultiTenantContext>>();

        if (httpContext.Request.Headers.TryGetValue(options.TenantIdHeaderName, out var tenantId))
        {
            string tenantIdValue = tenantId.ToString();

            if (logger?.IsEnabled(LogLevel.Information) == true)
            {
                logger.LogInformation(
                    "Delegate header strategy resolved tenant header. HeaderName={HeaderName}; HeaderValue={HeaderValue}; Path={Path}; TraceId={TraceId}",
                    options.TenantIdHeaderName,
                    tenantIdValue,
                    httpContext.Request.Path,
                    httpContext.TraceIdentifier);
            }

            return Task.FromResult<string?>(tenantIdValue);
        }

        if (logger?.IsEnabled(LogLevel.Warning) == true)
        {
            logger.LogWarning(
                "Delegate header strategy missing tenant header. HeaderName={HeaderName}; HeaderValue=<missing>; Path={Path}; TraceId={TraceId}",
                options.TenantIdHeaderName,
                httpContext.Request.Path,
                httpContext.TraceIdentifier);
        }

        return Task.FromResult<string?>(null);
    }
}
