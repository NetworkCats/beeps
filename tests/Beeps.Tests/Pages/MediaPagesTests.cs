using System.Net;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Media;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

/// <summary>Images on the web: posting them, showing them, and avatars on the settings and agent pages.</summary>
public sealed class MediaPagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    public MediaPagesTests() =>
        factory.Media.LockOwnedAsync(default, default!, CancellationToken.None)
            .ReturnsForAnyArgs(ci => ci.Arg<IReadOnlyCollection<Guid>>() is { } ids ? (IReadOnlyList<Guid>)[.. ids] : []);

    [Fact]
    public async Task ABeepCanBePostedWithImagesAndNoText()
    {
        var user = WebAuth.ArrangeUser(factory);
        Beep? posted = null;
        factory.Beeps.When(b => b.AddBeep(Arg.Any<Beep>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>())).Do(ci => posted = ci.Arg<Beep>());
        factory.Beeps.GetRowsAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(_ =>
            posted is null ? [] : (IReadOnlyList<BeepRow>)[new BeepRow(posted.Id, posted.Text, TestBeeps.Human(id: user.Id), null, posted.Id, TestData.Now, 0, 0, false)]);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await PostMultipartAsync(client, "/", "/compose", [("text", ""), ("ReturnUrl", "/")], [TestImages.Png(), TestImages.AnimatedGif()]);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("", posted!.Text);
        factory.Beeps.Received(1).AttachMedia(posted.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2));
        factory.Media.Received(2).AddMedia(Arg.Is<MediaItem>(m => m.OwnerActorId == user.Id));
    }

    [Fact]
    public async Task TooManyImagesComeBackWithTheTextAndNothingIsUploaded()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await PostMultipartAsync(client, "/", "/compose", [("text", "five")], [.. Enumerable.Range(0, 5).Select(_ => TestImages.Png())]);
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(">five</textarea>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"Images-error\"", html, StringComparison.Ordinal);
        await factory.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AFileThatIsNoImageIsReported()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await PostMultipartAsync(client, "/", "/compose", [("text", "look")], ["<html>"u8.ToArray()]);
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Only JPEG, PNG, WebP and GIF images can be uploaded.", html, StringComparison.Ordinal);
        factory.Beeps.DidNotReceiveWithAnyArgs().AddBeep(default!, default!, default!);
    }

    [Fact]
    public async Task TimelinesShowThumbnailsLinkingToTheFullImages()
    {
        var withImages = TestBeeps.Row("look", TestBeeps.Human());
        var imageOnly = TestBeeps.Row("", TestBeeps.Human() with { AvatarKey = "face" });
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([withImages, imageOnly], null));
        factory.Beeps.GetMediaAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<MediaRow>>
        {
            [withImages.Id] = [new(Guid.NewGuid(), "one", 800, 600), new(Guid.NewGuid(), "two", 600, 800)],
            [imageOnly.Id] = [new(Guid.NewGuid(), "three", 100, 100)],
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<div class=\"beep-media beep-media-2\">", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://media.beeps.test/one/full.webp\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"https://media.beeps.test/one/thumb.webp\" alt=\"Image 1 of 2\" width=\"800\" height=\"600\"", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"beep-media beep-media-1\">", html, StringComparison.Ordinal);
        Assert.Contains("src=\"https://media.beeps.test/face/avatar.webp\"", html, StringComparison.Ordinal);
        // An image-only beep has no empty text block.
        Assert.Equal(1, html.Split("class=\"beep-text\"").Length - 1);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task TheComposerOffersAnImagePicker()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("enctype=\"multipart/form-data\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"file\" name=\"Images\" multiple", html, StringComparison.Ordinal);
        Assert.Contains("data-max-files=\"4\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APersonUploadsAnAvatarOnTheSettingsPage()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Avatars.FindActorAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(user.Id, ActorKind.Human, null));
        Guid? uploaded = null;
        factory.Media.When(m => m.AddMedia(Arg.Any<MediaItem>())).Do(ci => uploaded = ci.Arg<MediaItem>().Id);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await PostMultipartAsync(client, "/settings", "/settings?handler=Avatar", [], [TestImages.Png(200, 100)], "Avatar");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await factory.Avatars.Received(1).SetAvatarAsync(user.Id, uploaded, Arg.Any<CancellationToken>());
        await factory.Accounts.Received(1).ForgetAccountAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheSquarePickedInTheCropperIsWhatTheServerCrops()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Avatars.FindActorAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(user.Id, ActorKind.Human, null));
        MediaUpload? upload = null;
        factory.Media.When(m => m.AddUpload(Arg.Any<MediaUpload>())).Do(ci => upload = ci.Arg<MediaUpload>());
        using var client = await WebAuth.SignInAsync(factory, user);

        // The bottom-left quadrant of a 200 × 100 image.
        using var response = await PostMultipartAsync(
            client, "/settings", "/settings?handler=Avatar", [("Crop.X", "0"), ("Crop.Y", "50"), ("Crop.Size", "50")], [TestImages.Quadrants(200, 100)], "Avatar");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new AvatarCrop(0, 50, 50), upload!.Crop);
        // What the processing job will make of it.
        var avatar = new SkiaImageProcessor().Encode(upload.Data, upload.Crop)!.Avatar;
        Assert.Equal((50, 50), TestImages.SizeOf(avatar));
        Assert.True(TestImages.Near(TestImages.ColorAt(avatar, 0.5f, 0.5f), TestImages.BottomLeft));
    }

    [Fact]
    public async Task ACropOutsideTheImageIsReported()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Avatars.FindActorAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(user.Id, ActorKind.Human, null));
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await PostMultipartAsync(
            client, "/settings", "/settings?handler=Avatar", [("Crop.X", "180"), ("Crop.Y", "0"), ("Crop.Size", "90")], [TestImages.Quadrants(200, 100)], "Avatar");
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("The selected area is outside the image or too small.", html, StringComparison.Ordinal);
        await factory.Avatars.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    [Theory]
    [InlineData("/settings")]
    [InlineData("/agents/{0}")]
    public async Task TheAvatarFormsLoadTheCropper(string path)
    {
        var user = WebAuth.ArrangeUser(factory);
        var agentId = Guid.NewGuid();
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
            [new AgentRow(agentId, "tom.claude", "tom's Claude", "Claude", TestData.Now, null, "beeps_abc", TestData.Now, TestData.Now)]);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, agentId), UriKind.Relative), TestSupport.Ct);

        Assert.Matches("<script type=\"module\" src=\"/dist/avatar-crop\\.[a-z0-9]+\\.js\"", html);
        Assert.Matches("<link rel=\"stylesheet\" href=\"/dist/avatar-crop\\.[a-z0-9]+\\.css\"", html);
        Assert.Contains("data-avatar-cropper", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Crop.Size\"", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task OtherPagesDoNotLoadTheCropper()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.DoesNotContain("avatar-crop", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSettingsPageAndNavigationShowTheCustomAvatar()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Accounts.GetAccountStateAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(new AccountState(user.Id, "tom", "tom", false, true, "en", AvatarKey: "face"));
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);

        // The avatar editor, the navigation rail and the narrow screens' top bar.
        Assert.Equal(3, html.Split("https://media.beeps.test/face/avatar.webp").Length - 1);
        Assert.Contains("handler=RemoveAvatar", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task WithTheDefaultAvatarThereIsNothingToRemove()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);

        Assert.Contains($"https://beeps.test/avatars/{user.Id:D}.svg", html, StringComparison.Ordinal);
        Assert.DoesNotContain("handler=RemoveAvatar", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABadAvatarIsReportedOnTheSettingsPage()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Avatars.FindActorAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(user.Id, ActorKind.Human, null));
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await PostMultipartAsync(client, "/settings", "/settings?handler=Avatar", [], [TestImages.Png(8, 8)], "Avatar");
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("An image must be at least 16 pixels on each side.", html, StringComparison.Ordinal);
        await factory.Avatars.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task AnOwnerChangesAndRemovesTheirAgentsAvatar()
    {
        var user = WebAuth.ArrangeUser(factory);
        var agentId = Guid.NewGuid();
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
            [new AgentRow(agentId, "tom.claude", "tom's Claude", "Claude", TestData.Now, null, "beeps_abc", TestData.Now, TestData.Now, AvatarKey: "face")]);
        factory.Avatars.FindActorAsync(agentId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(agentId, ActorKind.Agent, user.Id));
        using var client = await WebAuth.SignInAsync(factory, user);

        var page = await client.GetStringAsync(new Uri($"/agents/{agentId}", UriKind.Relative), TestSupport.Ct);
        using var set = await PostMultipartAsync(client, $"/agents/{agentId}", $"/agents/{agentId}?handler=Avatar", [], [TestImages.Png()], "Avatar");
        using var removed = await WebAuth.PostFormAsync(client, $"/agents/{agentId}", $"/agents/{agentId}?handler=RemoveAvatar");

        Assert.Contains("https://media.beeps.test/face/avatar.webp", page, StringComparison.Ordinal);
        Assert.Contains("handler=RemoveAvatar", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, set.StatusCode);
        Assert.Equal($"/agents/{agentId}", set.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        await factory.Avatars.Received(1).SetAvatarAsync(agentId, Arg.Is<Guid?>(id => id != null), Arg.Any<CancellationToken>());
        await factory.Avatars.Received(1).SetAvatarAsync(agentId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NobodyElseCanChangeAnAgentsAvatar()
    {
        var user = WebAuth.ArrangeUser(factory);
        var agentId = Guid.NewGuid();
        factory.Avatars.FindActorAsync(agentId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(agentId, ActorKind.Agent, Guid.NewGuid()));
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await PostMultipartAsync(client, "/settings", $"/agents/{agentId}?handler=Avatar", [], [TestImages.Png()], "Avatar");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await factory.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
    }

    public void Dispose() => factory.Dispose();

    private static async Task<HttpResponseMessage> PostMultipartAsync(
        HttpClient client, string pageUrl, string postUrl, (string Name, string Value)[] fields, byte[][] files, string fileField = MediaFields.Images)
    {
        var html = await client.GetStringAsync(new Uri(pageUrl, UriKind.Relative), TestSupport.Ct);
        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value), name);
        }

        for (var i = 0; i < files.Length; i++)
        {
            form.Add(new ByteArrayContent(files[i]), fileField, $"image{i}.png");
        }

        form.Add(new StringContent(TestSupport.AntiforgeryToken(html)), "__RequestVerificationToken");
        return await client.PostAsync(new Uri(postUrl, UriKind.Relative), form, TestSupport.Ct);
    }
}
