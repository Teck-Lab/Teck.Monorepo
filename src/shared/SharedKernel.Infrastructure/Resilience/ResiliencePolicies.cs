using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace SharedKernel.Infrastructure.Resilience;

public sealed class ResiliencePolicies
{
    public static HttpRetryStrategyOptions DefaultRetryPolicy { get; } = new()
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(100),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = false,
    };

    public static HttpCircuitBreakerStrategyOptions DefaultCircuitBreakerPolicy { get; } = new()
    {
        FailureRatio = 1,
        MinimumThroughput = 5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(30),
    };

    public static HttpTimeoutStrategyOptions DefaultTimeoutPolicy { get; } = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public static Action<HttpStandardResilienceOptions> DefaultCombinedPolicy { get; } = options =>
    {
        options.Retry.MaxRetryAttempts = DefaultRetryPolicy.MaxRetryAttempts;
        options.Retry.Delay = DefaultRetryPolicy.Delay;
        options.Retry.BackoffType = DefaultRetryPolicy.BackoffType;
        options.Retry.UseJitter = DefaultRetryPolicy.UseJitter;

        options.CircuitBreaker.FailureRatio = DefaultCircuitBreakerPolicy.FailureRatio;
        options.CircuitBreaker.MinimumThroughput = DefaultCircuitBreakerPolicy.MinimumThroughput;
        options.CircuitBreaker.SamplingDuration = DefaultCircuitBreakerPolicy.SamplingDuration;
        options.CircuitBreaker.BreakDuration = DefaultCircuitBreakerPolicy.BreakDuration;

        options.AttemptTimeout.Timeout = DefaultTimeoutPolicy.Timeout;
        options.TotalRequestTimeout.Timeout = DefaultTimeoutPolicy.Timeout;
    };
}

public static class ResiliencePoliciesExtensions
{
    public static IHttpClientBuilder AddTeckHttpResilience(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddStandardResilienceHandler(ResiliencePolicies.DefaultCombinedPolicy);
        return builder;
    }
}
