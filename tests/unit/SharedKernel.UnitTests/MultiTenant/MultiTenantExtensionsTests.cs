// <copyright file="MultiTenantExtensionsTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Reflection;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Infrastructure.MultiTenant;
using Xunit;

namespace SharedKernel.UnitTests.MultiTenant;

/// <summary>Regression tests for multi-tenant strategy logging.</summary>
public sealed class MultiTenantExtensionsTests
{
    /// <summary>Missing tenant headers must not expose request paths in warning logs.</summary>
    [Fact]
    public async Task ResolveHeaderStrategy_WhenTenantHeaderIsMissing_DoesNotLogRequestPath()
    {
        const string hostileMarker = "hostile-path-marker";
        const string traceId = "safe-trace-id";
        const string tenantHeaderName = "X-Custom-TenantId";
        var logger = new CapturingLogger();
        var services = new ServiceCollection()
            .AddSingleton<ILogger<IMultiTenantContext>>(logger)
            .Configure<TeckCloudMultiTenancyOptions>(options => options.TenantIdHeaderName = tenantHeaderName)
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = traceId,
        };
        context.Request.Path = $"/sensitive/{hostileMarker}";

        Task<string?> result = InvokeResolveHeaderStrategy(context);

        Assert.Null(await result.ConfigureAwait(false));

        CapturedLogEntry entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Null(entry.Exception);
        Assert.Equal(tenantHeaderName, entry.Values["HeaderName"]);
        Assert.Equal(traceId, entry.Values["TraceId"]);
        Assert.False(entry.Values.ContainsKey("Path"));
        Assert.All(entry.Values.Values, value => Assert.False(value?.ToString()?.Contains(hostileMarker, StringComparison.Ordinal) ?? false));
        Assert.Contains($"HeaderName={tenantHeaderName}", entry.RenderedMessage, StringComparison.Ordinal);
        Assert.Contains("HeaderValue=<missing>", entry.RenderedMessage, StringComparison.Ordinal);
        Assert.Contains($"TraceId={traceId}", entry.RenderedMessage, StringComparison.Ordinal);
        Assert.False(entry.RenderedMessage.Contains(hostileMarker, StringComparison.Ordinal));
        Assert.Contains("HeaderName={HeaderName}", entry.OriginalFormat, StringComparison.Ordinal);
        Assert.Contains("HeaderValue=<missing>", entry.OriginalFormat, StringComparison.Ordinal);
        Assert.Contains("TraceId={TraceId}", entry.OriginalFormat, StringComparison.Ordinal);
        Assert.False(entry.OriginalFormat.Contains("{Path}", StringComparison.Ordinal));
    }

    private static Task<string?> InvokeResolveHeaderStrategy(HttpContext context)
    {
        MethodInfo? method = typeof(MultiTenantExtensions).GetMethod(
            "ResolveHeaderStrategy",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return Assert.IsType<Task<string?>>(method.Invoke(null, [context]));
    }

    private sealed class CapturingLogger : ILogger<IMultiTenantContext>
    {
        public List<CapturedLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> structuredState
                ? structuredState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>();
            string originalFormat = Assert.IsType<string>(values["{OriginalFormat}"]);

            Entries.Add(new CapturedLogEntry(logLevel, values, originalFormat, exception, formatter(state, exception)));
        }
    }

    private sealed record CapturedLogEntry(
        LogLevel LogLevel,
        IReadOnlyDictionary<string, object?> Values,
        string OriginalFormat,
        Exception? Exception,
        string RenderedMessage);
}
