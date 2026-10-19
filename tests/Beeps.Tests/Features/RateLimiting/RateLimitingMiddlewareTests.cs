using System.Net;
using System.Security.Claims;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared.Http;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Features.RateLimiting;

public sealed class RateLimitingMiddlewareTests
{
    private readonly IRateLimitStore store = Substitute.For<IRateLimitStore>();
    private bool nextCalled;

    public RateLimitingMiddlewareTests()
    {
        store.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Allowed);
    }

    [Fact]
    public async Task AnonymousRequestsUseTheAnonymousBrowsePolicy()
    {
        await InvokeAsync(NewContext());

        Assert.True(nextCalled);
        await ReceivedPolicy(RateLimitPolicies.AnonymousBrowse);
    }

    [Fact]
    public async Task SignedInRequestsUseTheBrowsePolicy()
    {
        var context = NewContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))], "test"));

        await InvokeAsync(context);

        await ReceivedPolicy(RateLimitPolicies.Browse);
    }

    [Fact]
    public async Task EndpointPolicyWins()
    {
        var context = NewContext(new RateLimitPolicyAttribute(RateLimitPolicies.Register));

        await InvokeAsync(context);

        await ReceivedPolicy(RateLimitPolicies.Register);
    }

    [Fact]
    public async Task SkippedEndpointsAreNotCounted()
    {
        await InvokeAsync(NewContext(new SkipRateLimitAttribute()));

        Assert.True(nextCalled);
        await store.DidNotReceiveWithAnyArgs().TryAcquireAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task DeniedRequestsGet429WithRetryAfterAndStop()
    {
        store.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromSeconds(12.5)));
        var context = NewContext();

        await InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("13", context.Response.Headers.RetryAfter.ToString());
    }

    private static DefaultHttpContext NewContext(params object[] endpointMetadata)
    {
        var services = new ServiceCollection().AddLogging().AddBeepsProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(endpointMetadata), "test"));
        return context;
    }

    private Task<RateLimitDecision> ReceivedPolicy(string policy) =>
        store.Received(1).TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(counters => counters.Single().Key.StartsWith(policy + ":", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());

    private Task InvokeAsync(HttpContext context)
    {
        var middleware = new RateLimitingMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        return middleware.InvokeAsync(context, TestData.RateLimits(store));
    }
}
