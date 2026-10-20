using System.Text.Json;
using Beeps.Features.Shared.Http;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Features.Shared;

public sealed class ErrorHttpResultTests
{
    [Theory]
    [InlineData(ErrorKind.Validation, 400)]
    [InlineData(ErrorKind.Unauthorized, 401)]
    [InlineData(ErrorKind.Forbidden, 403)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.RateLimited, 429)]
    public void MapsKindToStatusCode(ErrorKind kind, int expected)
    {
        var result = new Error(kind, "some_code").ToHttpResult();

        Assert.Equal(expected, result.StatusCode);
        Assert.Equal(expected, result.Value.Status);
        Assert.Equal("some_code", result.Value.Extensions[ProblemDetailsSetup.CodeKey]);
    }

    [Theory]
    [InlineData(0.2, "1")]
    [InlineData(59.1, "60")]
    [InlineData(120, "120")]
    public void RetryAfterRoundsUpToWholeSeconds(double seconds, string expected) =>
        Assert.Equal(expected, ErrorHttpResult.RetryAfterHeaderValue(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void ValidationFieldsBecomeValidationProblemDetails()
    {
        var fields = new Dictionary<string, string[]> { ["text"] = ["Too long."] };

        var result = Error.Validation("invalid_beep", "Check the fields.", fields).ToHttpResult();

        var problem = Assert.IsType<HttpValidationProblemDetails>(result.Value);
        Assert.Equal(["Too long."], problem.Errors["text"]);
        Assert.Equal("Check the fields.", problem.Detail);
    }

    [Fact]
    public async Task WritesProblemJsonWithRetryAfter()
    {
        var context = NewHttpContext();

        await Error.RateLimited(TimeSpan.FromSeconds(42), "wait").ToHttpResult().ExecuteAsync(context);

        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("42", context.Response.Headers.RetryAfter.ToString());
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestSupport.Ct);
        Assert.Equal("rate_limited", json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.TryGetProperty("trace_id", out _));
        Assert.False(json.RootElement.TryGetProperty("traceId", out _));
    }

    private static DefaultHttpContext NewHttpContext()
    {
        var services = new ServiceCollection().AddLogging().AddBeepsProblemDetails().BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
    }
}
