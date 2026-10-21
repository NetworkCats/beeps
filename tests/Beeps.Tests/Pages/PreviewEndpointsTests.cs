using System.Net;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Profiles;
using Beeps.Tests.Support;
using SkiaSharp;

namespace Beeps.Tests.Pages;

public sealed class PreviewEndpointsTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public async Task TheSitesPreviewIsAPngOfTheOpenGraphSize(string culture)
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri($"/previews/{culture}/site.png?v=1", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=3600", response.Headers.CacheControl?.ToString());
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        await AssertPreviewAsync(response);
    }

    [Fact]
    public async Task ALanguageTheSiteDoesNotHaveIsNotFound()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri("/previews/fr/site.png", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ABeepsPreviewDrawsItsAuthorsAvatarAndItsFirstImage()
    {
        var author = TestBeeps.Human() with { AvatarKey = "face" };
        var beep = TestBeeps.Row("Shipped dark mode 🌙 for #settings, thanks @bob", author);
        TestBeeps.Arrange(factory, beep);
        factory.Beeps.GetMediaAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<MediaRow>>
        {
            [beep.Id] = [new(Guid.NewGuid(), "one", 800, 600), new(Guid.NewGuid(), "two", 600, 800)],
        });
        factory.Storage.GetAsync("face/avatar.webp", Arg.Any<CancellationToken>()).Returns(TestImages.Quadrants(256, 256, SKEncodedImageFormat.Webp));
        factory.Storage.GetAsync("one/thumb.webp", Arg.Any<CancellationToken>()).Returns(TestImages.Quadrants(800, 600, SKEncodedImageFormat.Webp));
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri($"/previews/en/beeps/{beep.Id:D}.png", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertPreviewAsync(response);
        await factory.Storage.Received(1).GetAsync("face/avatar.webp", Arg.Any<CancellationToken>());
        await factory.Storage.Received(1).GetAsync("one/thumb.webp", Arg.Any<CancellationToken>());
        await factory.Storage.DidNotReceive().GetAsync("two/thumb.webp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APreviewIsDrawnOnceAndThenReadFromDisk()
    {
        var author = TestBeeps.Human() with { AvatarKey = "face" };
        var beep = TestBeeps.Row("Drawn once", author);
        TestBeeps.Arrange(factory, beep);
        factory.Storage.GetAsync("face/avatar.webp", Arg.Any<CancellationToken>()).Returns(TestImages.Quadrants(256, 256, SKEncodedImageFormat.Webp));
        using var client = WebAuth.NewClient(factory);
        var address = new Uri($"/previews/en/beeps/{beep.Id:D}.png", UriKind.Relative);

        var first = await client.GetByteArrayAsync(address, TestSupport.Ct);
        var second = await client.GetByteArrayAsync(address, TestSupport.Ct);

        Assert.Equal(first, second);
        await factory.Storage.Received(1).GetAsync("face/avatar.webp", Arg.Any<CancellationToken>());
        var kept = Assert.Single(Kept(beep.Id));
        Assert.Equal(first, await File.ReadAllBytesAsync(kept, TestSupport.Ct));
    }

    [Fact]
    public async Task ANewVersionTakesTheOldOnesPlaceAndAGoneBeepTakesItsPreviewsWithIt()
    {
        var beep = TestBeeps.Row("first", TestBeeps.Human());
        using var client = WebAuth.NewClient(factory);
        var address = new Uri($"/previews/en/beeps/{beep.Id:D}.png", UriKind.Relative);

        TestBeeps.Arrange(factory, beep);
        (await client.GetAsync(address, TestSupport.Ct)).Dispose();
        var before = Assert.Single(Kept(beep.Id));
        TestBeeps.Arrange(factory, beep with { Text = "edited" });
        (await client.GetAsync(address, TestSupport.Ct)).Dispose();
        var after = Assert.Single(Kept(beep.Id));
        TestBeeps.Arrange(factory);
        using var gone = await client.GetAsync(address, TestSupport.Ct);

        Assert.NotEqual(before, after);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Empty(Kept(beep.Id));
    }

    [Fact]
    public async Task ABeepSignedOutVisitorsCannotSeeHasNoPreview()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri($"/previews/en/beeps/{Guid.NewGuid():D}.png", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AProfilesPreviewIsFoundByIdAndDrawsItsBanner()
    {
        var agent = TestBeeps.Agent(Guid.NewGuid());
        factory.Profiles.FindHandleAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent.Handle);
        factory.Profiles.FindByHandleAsync(agent.Handle!, Arg.Any<CancellationToken>())
            .Returns(new ProfileRow(agent, "Posts what it ships.", null, TestData.Now, TestBeeps.Human(id: agent.OwnerId), BannerKey: "wide"));
        factory.Storage.GetAsync("wide/banner.webp", Arg.Any<CancellationToken>()).Returns(TestImages.Quadrants(1500, 500, SKEncodedImageFormat.Webp));
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri($"/previews/zh-Hans/actors/{agent.Id:D}.png", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertPreviewAsync(response);
        await factory.Storage.Received(1).GetAsync("wide/banner.webp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnActorThatIsGoneHasNoPreview()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri($"/previews/en/actors/{Guid.NewGuid():D}.png", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The versions of a beep's preview on disk, in every language.</summary>
    private string[] Kept(Guid beepId)
    {
        var folder = Path.Combine(factory.PreviewDirectory, "beeps", $"{beepId:D}"[..2]);
        return Directory.Exists(folder) ? Directory.GetFiles(folder, $"{beepId:D}.*") : [];
    }

    private static async Task AssertPreviewAsync(HttpResponseMessage response)
    {
        using var image = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync(TestSupport.Ct));
        Assert.Equal((1200, 630), (image.Width, image.Height));
    }
}
