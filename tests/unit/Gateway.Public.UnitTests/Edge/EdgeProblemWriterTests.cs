using System.Text.Json;
using Gateway.Public.Edge;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Gateway.Public.UnitTests.Edge;

/// <summary>Unit tests for <see cref="EdgeProblemWriter"/>.</summary>
public sealed class EdgeProblemWriterTests
{
    /// <summary>WriteAsync should write RFC-7807 problem+json with correct status and error code.</summary>
    [Fact]
    public async Task WritesProblemJson_WithStatusAndErrorCode()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        await EdgeProblemWriter.WriteAsync(ctx, new EdgeProblem(403, "Tenant mismatch", "nope", "tenant.mismatch"));

        Assert.Equal(403, ctx.Response.StatusCode);
        Assert.Equal("application/problem+json", ctx.Response.ContentType);
        ctx.Response.Body.Position = 0;
        using var doc = JsonDocument.Parse(ctx.Response.Body);
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("tenant.mismatch",
            doc.RootElement.GetProperty("errors")[0].GetProperty("name").GetString());
    }
}
