using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace SharedKernel.Infrastructure.Resilience;

/// <summary>
/// Provides the default HTTP resilience strategy options used across Teck services.
/// </summary>
public sealed class ResiliencePolicies
{
    /// <summary>
    /// Gets the default retry strategy options.
    /// </summary>
    public static HttpRetryStrategyOptions DefaultRetryPolicy { get; } = new()
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(100),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = false,
    };

    /// <summary>
    /// Gets the default circuit breaker strategy options.
    /// </summary>
    public static HttpCircuitBreakerStrategyOptions DefaultCircuitBreakerPolicy { get; } = new()
    {
        FailureRatio = 1,
        MinimumThroughput = 5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(30),
    };

    /// <summary>
    /// Gets the default timeout strategy options.
    /// </summary>
    public static HttpTimeoutStrategyOptions DefaultTimeoutPolicy { get; } = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>
    /// Gets the default combined resilience configuration that applies the retry,
    /// circuit breaker and timeout strategies to the standard resilience pipeline.
    /// </summary>
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
