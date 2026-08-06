// <copyright file="TenantRateLimitMiddlewareTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Infrastructure.Middlewares;
using Xunit;

namespace SharedKernel.UnitTests.Middlewares;

/// <summary>Security boundary tests for per-tenant request throttling.</summary>
public sealed class TenantRateLimitMiddlewareTests
{
    /// <summary>Exhausting one tenant must not consume another tenant's allowance.</summary>
    [Fact]
    public async Task InvokeAsync_WhenOneTenantIsExhausted_IsolatesOtherTenants()
    {
        int forwarded = 0;
        var middleware = CreateMiddleware(_ =>
        {
            forwarded++;
            return Task.CompletedTask;
        });

        DefaultHttpContext first = ContextFor("tenant-a");
        DefaultHttpContext blocked = ContextFor("tenant-a");
        DefaultHttpContext isolated = ContextFor("tenant-b");

        await middleware.InvokeAsync(first);
        await middleware.InvokeAsync(blocked);
        await middleware.InvokeAsync(isolated);

        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
        Assert.True(blocked.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal(StatusCodes.Status200OK, isolated.Response.StatusCode);
        Assert.Equal(2, forwarded);
    }

    /// <summary>Health endpoints must remain reachable even when the anonymous bucket is exhausted.</summary>
    [Fact]
    public async Task InvokeAsync_WhenHealthPathBypassesLimit_AlwaysContinues()
    {
        int forwarded = 0;
        var middleware = CreateMiddleware(_ =>
        {
            forwarded++;
            return Task.CompletedTask;
        });

        var first = new DefaultHttpContext();
        var second = new DefaultHttpContext();
        first.Request.Path = "/health/ready";
        second.Request.Path = "/health/ready";

        await middleware.InvokeAsync(first);
        await middleware.InvokeAsync(second);

        Assert.Equal(2, forwarded);
        Assert.Equal(StatusCodes.Status200OK, second.Response.StatusCode);
    }

    private static TenantRateLimitMiddleware CreateMiddleware(RequestDelegate next) =>
        new(
            next,
            Options.Create(new TenantRateLimitOptions
            {
                TokenLimit = 1,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromHours(1),
                QueueLimit = 0,
                AutoReplenishment = false,
            }),
            NullLogger<TenantRateLimitMiddleware>.Instance);

    private static DefaultHttpContext ContextFor(string tenant)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-TenantId"] = tenant;
        return context;
    }
}
