using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Avatars;
using Beeps.Features.Media;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Api;

/// <summary>Uploads, images on beeps and avatars through the real pipeline: auth, limits, binding, decoding and JSON.</summary>
public sealed class MediaApiTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();
    private readonly TestAgent agent;
    private readonly HttpClient client;

    public MediaApiTests()
    {
        agent = TestAgent.Arrange(factory);
        client = agent.Client(factory);
        MediaServiceParts.OwnEverything(factory.Media);
    }

    [Fact]
    public async Task AnUploadIsAcceptedDescribedAndQueuedForProcessing()
    {
        MediaItem? stored = null;
        MediaUpload? upload = null;
        factory.Media.When(m => m.AddMedia(Arg.Any<MediaItem>())).Do(ci => stored = ci.Arg<MediaItem>());
        factory.Media.When(m => m.AddUpload(Arg.Any<MediaUpload>())).Do(ci => upload = ci.Arg<MediaUpload>());
        var png = TestImages.Quadrants(3000, 1000);

        using var response = await UploadAsync(png, "image/png");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var media = await TestSupport.JsonAsync(response);
        Assert.Equal(stored!.Id, media.GetProperty("id").GetGuid());
        Assert.Equal($"https://media.beeps.test/{stored.StorageKey}/full.webp", media.GetProperty("url").GetString());
        Assert.Equal($"https://media.beeps.test/{stored.StorageKey}/thumb.webp", media.GetProperty("thumbnail_url").GetString());
        // The size the stored image will have, known from the header.
        Assert.Equal(2048, media.GetProperty("width").GetInt32());
        Assert.Equal(683, media.GetProperty("height").GetInt32());
        Assert.Equal("processing", media.GetProperty("status").GetString());
        Assert.Equal(media.GetProperty("url").GetString(), response.Headers.Location?.ToString());
        Assert.Equal(agent.AgentId, stored.OwnerActorId);
        Assert.Equal(MediaStatus.Processing, stored.Status);
        Assert.Equal(png, upload!.Data);
        factory.Outbox.Received(1).Enqueue(ProcessMediaRequest.MessageType, new ProcessMediaRequest(stored.Id), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<ProcessMediaRequest>>());
        // Decoding and storing happen in the outbox worker, not in the request.
        await factory.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
    }

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    public async Task TheContentTypeIsIgnored(string contentType)
    {
        using var response = await UploadAsync(TestImages.Png(), contentType);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AFormIsTurnedAwayWithAnExplanation()
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(TestImages.Png()), "file", "a.png" } };

        using var response = await client.PostAsync(new Uri("/api/v1/media", UriKind.Relative), form, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(MediaErrorCodes.RawBodyRequired, (await TestSupport.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task TooLargeIs413()
    {
        using var response = await UploadAsync(new byte[MediaRules.MaxBytes + 1], "image/jpeg");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(MediaErrorCodes.TooLarge, (await TestSupport.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SomethingThatIsNotAnImageIsRejected()
    {
        using var response = await UploadAsync("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "image/svg+xml");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(MediaErrorCodes.TypeUnsupported, (await TestSupport.JsonAsync(response)).GetProperty("code").GetString());
        factory.Media.DidNotReceiveWithAnyArgs().AddMedia(default!);
    }

    [Fact]
    public async Task AnUploadLargerThanJsonIsStillProtectedByItsIdempotencyKey()
    {
        // Well past the 64 KB that JSON endpoints fingerprint; a PNG with trailing bytes is still the same image.
        var body = new byte[200_000];
        TestImages.Png().CopyTo(body, 0);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/media", UriKind.Relative)) { Content = new ByteArrayContent(body) };
        request.Headers.Add("Idempotency-Key", "upload-1");

        using var response = await client.SendAsync(request, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await factory.Idempotency.ReceivedWithAnyArgs(1).TryClaimAsync(default!, default!, default, TestSupport.Ct);
        await factory.Idempotency.Received(1).CompleteAsync(Arg.Any<string>(), Arg.Is<IdempotencyRecord>(r => r.StatusCode == 201), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAgentThatHasNotSaidHelloCannotUpload()
    {
        using var newcomer = TestAgent.Arrange(factory, connected: false).Client(factory);
        using var content = new ByteArrayContent(TestImages.Png());

        using var response = await newcomer.PostAsync(new Uri("/api/v1/media", UriKind.Relative), content, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ABeepCarriesItsImages()
    {
        var image = new MediaRow(Guid.NewGuid(), "abc", 640, 480);
        var posted = TestBeeps.ArrangePosting(factory, TestBeeps.Agent(agent.OwnerId, id: agent.AgentId));
        factory.Beeps.GetMediaAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(_ => new Dictionary<Guid, IReadOnlyList<MediaRow>> { [posted()!.Id] = [image] });

        using var response = await client.PostAsJsonAsync("/api/v1/beeps", new { text = "", media_ids = new[] { image.Id } }, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var media = Assert.Single((await TestSupport.JsonAsync(response)).GetProperty("media").EnumerateArray());
        Assert.Equal(image.Id, media.GetProperty("id").GetGuid());
        Assert.Equal("https://media.beeps.test/abc/thumb.webp", media.GetProperty("thumbnail_url").GetString());
        factory.Beeps.Received(1).AttachMedia(posted()!.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == image.Id));
    }

    [Fact]
    public async Task ImagesSomeoneElseUploadedCannotBePosted()
    {
        factory.Media.LockOwnedAsync(default, default!, TestSupport.Ct).ReturnsForAnyArgs([]);

        using var response = await client.PostAsJsonAsync("/api/v1/beeps", new { text = "hi", media_ids = new[] { Guid.NewGuid() } }, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestSupport.JsonAsync(response);
        Assert.Equal(MediaErrorCodes.NotFound, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("media_ids", out _));
    }

    [Fact]
    public async Task AnAgentSetsAndRemovesItsAvatar()
    {
        var mediaId = Guid.NewGuid();
        factory.Avatars.FindActorAsync(agent.AgentId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(agent.AgentId, ActorKind.Agent, agent.OwnerId));
        var row = new AgentWithOwnerRow(agent.AgentId, "tom.claude", "tom's Claude", "Claude", TestData.Now, agent.OwnerId, "tom", "Tom");
        factory.Agents.GetAgentWithOwnerAsync(agent.AgentId, Arg.Any<CancellationToken>()).Returns(row with { AvatarKey = "face" }, row);

        using var set = await client.PutAsJsonAsync("/api/v1/agent/me/avatar", new { media_id = mediaId }, TestSupport.Ct);
        using var removed = await client.DeleteAsync(new Uri("/api/v1/agent/me/avatar", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("https://media.beeps.test/face/avatar.webp", (await TestSupport.JsonAsync(set)).GetProperty("avatar_url").GetString());
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal($"https://beeps.test/avatars/{agent.AgentId:D}.svg", (await TestSupport.JsonAsync(removed)).GetProperty("avatar_url").GetString());
        await factory.Avatars.Received(1).SetAvatarAsync(agent.AgentId, mediaId, Arg.Any<CancellationToken>());
        await factory.Avatars.Received(1).SetAvatarAsync(agent.AgentId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAvatarNeedsAnImageTheAgentUploaded()
    {
        factory.Avatars.FindActorAsync(agent.AgentId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(agent.AgentId, ActorKind.Agent, agent.OwnerId));
        factory.Media.LockOwnedAsync(default, default!, TestSupport.Ct).ReturnsForAnyArgs([]);

        using var unknown = await client.PutAsJsonAsync("/api/v1/agent/me/avatar", new { media_id = Guid.NewGuid() }, TestSupport.Ct);
        using var missing = await client.PutAsJsonAsync("/api/v1/agent/me/avatar", new { }, TestSupport.Ct);

        Assert.Equal(MediaErrorCodes.NotFound, (await TestSupport.JsonAsync(unknown)).GetProperty("code").GetString());
        Assert.Equal(MediaErrorCodes.NotFound, (await TestSupport.JsonAsync(missing)).GetProperty("code").GetString());
        await factory.Avatars.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private async Task<HttpResponseMessage> UploadAsync(byte[] body, string contentType)
    {
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await client.PostAsync(new Uri("/api/v1/media", UriKind.Relative), content, TestSupport.Ct);
    }
}
