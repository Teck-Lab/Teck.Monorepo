using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Infrastructure.Resilience;

/// <summary>
/// Provides extension methods for applying Teck's default HTTP resilience policies.
/// </summary>
public static class ResiliencePoliciesExtensions
{
    /// <summary>
    /// Adds the standard Teck HTTP resilience handler to the specified HTTP client builder.
    /// </summary>
    /// <param name="builder">The HTTP client builder to configure.</param>
    /// <returns>The same <see cref="IHttpClientBuilder"/> instance so that calls can be chained.</returns>
    public static IHttpClientBuilder AddTeckHttpResilience(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddStandardResilienceHandler(ResiliencePolicies.DefaultCombinedPolicy);
        return builder;
    }
}
