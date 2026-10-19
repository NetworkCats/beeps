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

    public MediaPagesTests() => MediaServiceParts.OwnEverything(factory.Media);

    [Fact]
    public async Task ABeepCanBePostedWithImagesAndNoText()
    {
        var user = WebAuth.ArrangeUser(factory);
        var posted = TestBeeps.ArrangePosting(factory, TestBeeps.Human(id: user.Id));
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await PostMultipartAsync(client, "/", "/compose", [("text", ""), ("ReturnUrl", "/")], [TestImages.Png(), TestImages.AnimatedGif()]);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("", posted()!.Text);
        factory.Beeps.Received(1).AttachMedia(posted()!.Id, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2));
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
        // The composer keeps the text, its image picker marked with why.
        var compose = Islands.Envelope(html, "compose");
        Assert.Equal("five", compose.GetProperty("props").GetProperty("composer").GetProperty("text").GetString());
        Assert.True(compose.GetProperty("errors").TryGetProperty("Images", out _));
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

        var page = await Islands.PageAsync(client, "/");

        // The composer, which the app draws with its image picker.
        Assert.True(Islands.Props(page, "index").TryGetProperty("composer", out _));
        var config = Islands.Config(page);
        // The app checks the picked images against the same limits before they are sent.
        Assert.Equal(MediaRules.MaxImagesPerBeep, config.GetProperty("limits").GetProperty("imagesPerBeep").GetInt32());
        Assert.Equal(MediaRules.MaxBytes, config.GetProperty("limits").GetProperty("mediaBytes").GetInt64());
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
    public async Task APersonUploadsABannerOnTheSettingsPageCutWhereTheyPicked()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Avatars.FindActorAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(user.Id, ActorKind.Human, null));
        factory.Avatars.FindBannerKeyAsync(user.Id, Arg.Any<CancellationToken>()).Returns("wide");
        Guid? uploaded = null;
        MediaUpload? upload = null;
        factory.Media.When(m => m.AddMedia(Arg.Any<MediaItem>())).Do(ci => uploaded = ci.Arg<MediaItem>().Id);
        factory.Media.When(m => m.AddUpload(Arg.Any<MediaUpload>())).Do(ci => upload = ci.Arg<MediaUpload>());
        using var client = await WebAuth.SignInAsync(factory, user);

        var page = await Islands.PageAsync(client, "/settings");
        // A strip of the bottom-right quadrant of a 400 × 200 image.
        using var response = await PostMultipartAsync(
            client, "/settings", "/settings?handler=Banner", [("BannerCrop.X", "210"), ("BannerCrop.Y", "120"), ("BannerCrop.Width", "150")], [TestImages.Quadrants(400, 200)], "Banner");
        using var removed = await WebAuth.PostFormAsync(client, "/settings", "/settings?handler=RemoveBanner");

        Assert.Equal("https://media.beeps.test/wide/banner.webp", Islands.Props(page, "settings").GetProperty("banner").GetProperty("bannerUrl").GetString());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new BannerCrop(210, 120, 150), upload!.BannerCrop);
        Assert.Null(upload.Crop);
        // What the processing job will make of it.
        var banner = new SkiaImageProcessor().Encode(upload.Data, upload.Crop, upload.BannerCrop)!.Banner!;
        Assert.Equal((150, 50), TestImages.SizeOf(banner));
        Assert.True(TestImages.Near(TestImages.ColorAt(banner, 0.5f, 0.5f), TestImages.BottomRight));
        await factory.Avatars.Received(1).SetBannerAsync(user.Id, uploaded, Arg.Any<CancellationToken>());
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        await factory.Avatars.Received(1).SetBannerAsync(user.Id, null, Arg.Any<CancellationToken>());
        await factory.Avatars.DidNotReceiveWithAnyArgs().SetAvatarAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task ABannerCropOutsideTheImageIsReported()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Avatars.FindActorAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(user.Id, ActorKind.Human, null));
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await PostMultipartAsync(
            client, "/settings", "/settings?handler=Banner", [("BannerCrop.X", "0"), ("BannerCrop.Y", "0"), ("BannerCrop.Width", "600")], [TestImages.Quadrants(400, 200)], "Banner");
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("The selected strip is outside the image or too small.", html, StringComparison.Ordinal);
        await factory.Avatars.DidNotReceiveWithAnyArgs().SetBannerAsync(default, default, TestSupport.Ct);
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
    [InlineData("/settings/profile")]
    [InlineData("/agents/{0}")]
    public async Task TheAvatarFormsLoadTheCroppersStyles(string path)
    {
        var user = WebAuth.ArrangeUser(factory);
        var agentId = Guid.NewGuid();
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
            [new AgentRow(agentId, "tom.claude", "tom's Claude", "Claude", TestData.Now, null, "beeps_abc", TestData.Now)]);
        using var client = await WebAuth.SignInAsync(factory, user);

        var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, path, agentId);
        var html = await client.GetStringAsync(new Uri(url, UriKind.Relative), TestSupport.Ct);
        var page = await Islands.PageAsync(client, url);

        // The cropper itself comes with the app, once a file is picked; its stylesheet is fetched ahead, and linked as the page is shown.
        Assert.Matches("<link rel=\"prefetch\" href=\"/dist/image-crop\\.[a-z0-9]+\\.css\" data-style=\"image-crop\"", html);
        Assert.Contains("image-crop", Islands.Strings(Islands.Data(page).GetProperty("shell").GetProperty("styles")));
        // The avatar form (with the crop the cropper picks), posting back to the page.
        Assert.Equal(url, Islands.Props(page, path == "/settings/profile" ? "settings" : "agent").GetProperty("avatar").GetProperty("action").GetString());
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task OtherPagesOnlyFetchTheCropperAhead()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.DoesNotMatch("<link rel=\"stylesheet\" href=\"/dist/image-crop", html);
        Assert.Matches("<link rel=\"prefetch\" href=\"/dist/image-crop\\.[a-z0-9]+\\.css\"", html);
    }

    [Fact]
    public async Task TheSettingsPageAndNavigationShowTheCustomAvatar()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Accounts.GetAccountStateAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(new AccountState(user.Id, "tom", "tom", false, true, "en", AvatarKey: "face"));
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);
        var page = await Islands.PageAsync(client, "/settings");

        // The avatar editor, which offers to remove it, and the account menus of the navigation rail and the narrow
        // screens' top bar, drawn from who is signed in.
        var avatar = Islands.Props(page, "settings").GetProperty("avatar");
        Assert.Equal("https://media.beeps.test/face/avatar.webp", avatar.GetProperty("avatarUrl").GetString());
        Assert.True(avatar.GetProperty("hasCustomAvatar").GetBoolean());
        Assert.Equal(2, Islands.Names(html).Count(name => name == "account-menu"));
        Assert.Equal("https://media.beeps.test/face/avatar.webp", Islands.Config(page).GetProperty("viewer").GetProperty("avatar").GetString());
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task WithTheDefaultAvatarThereIsNothingToRemove()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var page = await Islands.PageAsync(client, "/settings");

        var avatar = Islands.Props(page, "settings").GetProperty("avatar");
        Assert.Equal($"https://beeps.test/avatars/{user.Id:D}.svg", avatar.GetProperty("avatarUrl").GetString());
        Assert.False(avatar.TryGetProperty("hasCustomAvatar", out _));
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
            [new AgentRow(agentId, "tom.claude", "tom's Claude", "Claude", TestData.Now, null, "beeps_abc", TestData.Now, AvatarKey: "face")]);
        factory.Avatars.FindActorAsync(agentId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(agentId, ActorKind.Agent, user.Id));
        using var client = await WebAuth.SignInAsync(factory, user);

        var page = await Islands.PageAsync(client, $"/agents/{agentId}");
        using var set = await PostMultipartAsync(client, $"/agents/{agentId}", $"/agents/{agentId}?handler=Avatar", [], [TestImages.Png()], "Avatar");
        using var removed = await WebAuth.PostFormAsync(client, $"/agents/{agentId}", $"/agents/{agentId}?handler=RemoveAvatar");

        var avatar = Islands.Props(page, "agent").GetProperty("avatar");
        Assert.Equal("https://media.beeps.test/face/avatar.webp", avatar.GetProperty("avatarUrl").GetString());
        // So it can be removed.
        Assert.True(avatar.GetProperty("hasCustomAvatar").GetBoolean());
        Assert.Equal(HttpStatusCode.Redirect, set.StatusCode);
        Assert.Equal($"/agents/{agentId}", set.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        await factory.Avatars.Received(1).SetAvatarAsync(agentId, Arg.Is<Guid?>(id => id != null), Arg.Any<CancellationToken>());
        await factory.Avatars.Received(1).SetAvatarAsync(agentId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOwnerChangesAndRemovesTheirAgentsBanner()
    {
        var user = WebAuth.ArrangeUser(factory);
        var agentId = Guid.NewGuid();
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
            [new AgentRow(agentId, "tom.claude", "tom's Claude", "Claude", TestData.Now, null, "beeps_abc", TestData.Now)]);
        factory.Avatars.FindActorAsync(agentId, Arg.Any<CancellationToken>()).Returns(new AvatarTarget(agentId, ActorKind.Agent, user.Id));
        factory.Avatars.FindBannerKeyAsync(agentId, Arg.Any<CancellationToken>()).Returns("wide");
        using var client = await WebAuth.SignInAsync(factory, user);

        var page = await Islands.PageAsync(client, $"/agents/{agentId}");
        using var set = await PostMultipartAsync(client, $"/agents/{agentId}", $"/agents/{agentId}?handler=Banner", [], [TestImages.Png(300, 100)], "Banner");
        using var removed = await WebAuth.PostFormAsync(client, $"/agents/{agentId}", $"/agents/{agentId}?handler=RemoveBanner");

        Assert.Equal("https://media.beeps.test/wide/banner.webp", Islands.Props(page, "agent").GetProperty("banner").GetProperty("bannerUrl").GetString());
        Assert.Equal(HttpStatusCode.Redirect, set.StatusCode);
        Assert.Equal($"/agents/{agentId}", set.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        await factory.Avatars.Received(1).SetBannerAsync(agentId, Arg.Is<Guid?>(id => id != null), Arg.Any<CancellationToken>());
        await factory.Avatars.Received(1).SetBannerAsync(agentId, null, Arg.Any<CancellationToken>());
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
        var token = await WebAuth.TokenAsync(client, pageUrl);
        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value), name);
        }

        for (var i = 0; i < files.Length; i++)
        {
            form.Add(new ByteArrayContent(files[i]), fileField, $"image{i}.png");
        }

        form.Add(new StringContent(token), "__RequestVerificationToken");
        return await client.PostAsync(new Uri(postUrl, UriKind.Relative), form, TestSupport.Ct);
    }
}
