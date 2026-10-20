using System.Net;
using System.Net.Http.Json;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Tests.Support;

namespace Beeps.Tests.Api;

public sealed class IdempotencyTests : IDisposable
{
    private const string Path = "/api/v1/notifications/read";

    private readonly BeepsWebApplicationFactory factory = new();
    private readonly TestAgent agent;
    private readonly HttpClient client;

    public IdempotencyTests()
    {
        agent = TestAgent.Arrange(factory);
        client = agent.Client(factory);
    }

    [Fact]
    public async Task TheFirstRequestRunsAndItsAnswerIsKept()
    {
        using var response = await PostAsync("key-1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await factory.Notifications.ReceivedWithAnyArgs(1).MarkReadAsync(default, default, default, TestSupport.Ct);
        await factory.Idempotency.Received(1).CompleteAsync(
            Arg.Is<string>(k => k.StartsWith($"{agent.AgentId:N}:", StringComparison.Ordinal)),
            Arg.Is<IdempotencyRecord>(r => r.StatusCode == 204),
            TimeSpan.FromHours(24),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARetryGetsTheSameAnswerWithoutRunning()
    {
        string? fingerprint = null;
        factory.Idempotency.TryClaimAsync(default!, default!, default, TestSupport.Ct).ReturnsForAnyArgs(ci =>
        {
            if (fingerprint is null)
            {
                fingerprint = ci.ArgAt<string>(1);
                return null;
            }

            return new IdempotencyRecord(fingerprint, 201, "application/json; charset=utf-8", "/api/v1/beeps/x", """{"id":"x"}""");
        });

        using var first = await PostAsync("key-2");
        using var retry = await PostAsync("key-2");

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal("/api/v1/beeps/x", retry.Headers.Location?.ToString());
        Assert.Equal("""{"id":"x"}""", await retry.Content.ReadAsStringAsync(TestSupport.Ct));
        await factory.Notifications.ReceivedWithAnyArgs(1).MarkReadAsync(default, default, default, TestSupport.Ct);
    }

    [Theory]
    [InlineData(false, 204, HttpStatusCode.BadRequest, "idempotency_key_reused")]
    [InlineData(true, null, HttpStatusCode.Conflict, "idempotency_key_in_use")]
    public async Task AKeyCannotBeReusedForAnotherRequestOrWhileRunning(bool sameRequest, int? storedStatus, HttpStatusCode status, string code)
    {
        factory.Idempotency.TryClaimAsync(default!, default!, default, TestSupport.Ct).ReturnsForAnyArgs(ci =>
            new IdempotencyRecord(sameRequest ? ci.ArgAt<string>(1) : "another request", storedStatus, null, null, null));

        using var response = await PostAsync("key-3");

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
        await factory.Notifications.DidNotReceiveWithAnyArgs().MarkReadAsync(default, default, default, TestSupport.Ct);
    }

    public static TheoryData<string> BadKeys => [new string('k', IdempotencyMiddleware.MaxKeyLength + 1), "key with spaces", "clé"];

    [Theory]
    [MemberData(nameof(BadKeys))]
    public async Task KeysMustBeShortVisibleAscii(string key)
    {
        using var response = await PostAsync(key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_invalid", await CodeAsync(response));
        await factory.Notifications.DidNotReceiveWithAnyArgs().MarkReadAsync(default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task AKeyAsLongAsAllowedIsKept()
    {
        using var response = await PostAsync(new string('k', IdempotencyMiddleware.MaxKeyLength));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await factory.Idempotency.ReceivedWithAnyArgs(1).CompleteAsync(default!, default!, default, TestSupport.Ct);
    }

    [Fact]
    public async Task FailuresAreForgottenSoTheyCanBeRetried()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/beeps") { Content = JsonContent.Create(new { text = "" }), Headers = { { "Idempotency-Key", "key-4" } } };
        using var response = await client.SendAsync(request, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.Idempotency.Received(1).ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await factory.Idempotency.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default, TestSupport.Ct);
    }

    [Fact]
    public async Task TheWriteIsNotCancelledWhenTheCallerGoesAway()
    {
        var token = new CancellationToken(canceled: true);
        factory.Notifications.MarkReadAsync(default, default, default, TestSupport.Ct).ReturnsForAnyArgs(ci =>
        {
            token = ci.ArgAt<CancellationToken>(3);
            return Task.FromResult(0);
        });

        using var response = await PostAsync("key-7");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(token.CanBeCanceled);
    }

    [Fact]
    public async Task WithValkeyDownTheRequestStillRuns()
    {
        factory.Idempotency.TryClaimAsync(default!, default!, default, TestSupport.Ct).ReturnsForAnyArgs<IdempotencyRecord?>(_ => throw new InvalidOperationException("down"));

        using var response = await PostAsync("key-5");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReadsIgnoreTheHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/timelines/public") { Headers = { { "Idempotency-Key", "key-6" } } };

        using var response = await client.SendAsync(request, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await factory.Idempotency.DidNotReceiveWithAnyArgs().TryClaimAsync(default!, default!, default, TestSupport.Ct);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private async Task<HttpResponseMessage> PostAsync(string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(new { up_to_id = (Guid?)null }) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request, TestSupport.Ct);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        return json.RootElement.GetProperty("code").GetString();
    }
}
