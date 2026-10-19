using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Beeps.Features.Admin;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Pages;

/// <summary>What a page loads of the client: its stylesheets by who is looking, and the script chunks it needs.</summary>
public sealed partial class ClientAssetsTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task SignedOutPagesLinkTheMainStylesheetAndFetchTheOthersAhead()
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(["main"], Stylesheets(html));
        Assert.Equal(["member", "image-crop"], Prefetched(html));
    }

    [Fact]
    public async Task SignedInPagesLinkTheMembersStylesheet()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(["main", "member"], Stylesheets(html));
        Assert.Equal(["image-crop"], Prefetched(html));
    }

    [Fact]
    public async Task AdminPagesLinkTheAdminStylesheetAndAnAdminsOtherPagesFetchItAhead()
    {
        var admin = WebAuth.ArrangeUser(factory, admin: true);
        factory.Admin.ListUsersAsync(null, null, null, AdminService.PageSize, Arg.Any<CancellationToken>()).Returns(new CursorPage<AdminUserRow>([], null));
        using var client = await WebAuth.SignInAsync(factory, admin);

        var adminPage = await client.GetStringAsync(new Uri("/admin/users", UriKind.Relative), TestSupport.Ct);
        var otherPage = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(["main", "member", "admin"], Stylesheets(adminPage));
        Assert.Equal(["image-crop"], Prefetched(adminPage));
        Assert.Equal(["main", "member"], Stylesheets(otherPage));
        Assert.Equal(["admin", "image-crop"], Prefetched(otherPage));
    }

    [Fact]
    public async Task TheAdminPagesScriptsAndStylesheetAreForAdminsOnly()
    {
        using var manifest = await ManifestAsync();
        var adminChunk = manifest.RootElement.GetProperty("admin").EnumerateArray().Select(c => c.GetString()!).First(c => c.StartsWith("chunks/", StringComparison.Ordinal));
        var publicChunk = manifest.RootElement.GetProperty("app")[0].GetString()!;
        var admin = WebAuth.ArrangeUser(factory, handle: "root", admin: true);
        var member = WebAuth.ArrangeUser(factory);
        factory.Admin.ListUsersAsync(null, null, null, AdminService.PageSize, Arg.Any<CancellationToken>()).Returns(new CursorPage<AdminUserRow>([], null));
        using var asAdmin = await WebAuth.SignInAsync(factory, admin);
        using var asMember = await WebAuth.SignInAsync(factory, member);
        using var anonymous = WebAuth.NewClient(factory);
        var stylesheet = AdminStylesheet().Match(await asAdmin.GetStringAsync(new Uri("/admin/users", UriKind.Relative), TestSupport.Ct)).Value;

        foreach (var file in new[] { stylesheet, "/dist/admin.css", $"/dist/{adminChunk}" })
        {
            using var forAdmin = await asAdmin.GetAsync(new Uri(file, UriKind.Relative), TestSupport.Ct);
            using var forMember = await asMember.GetAsync(new Uri(file, UriKind.Relative), TestSupport.Ct);
            using var forAnyone = await anonymous.GetAsync(new Uri(file, UriKind.Relative), TestSupport.Ct);
            Assert.Equal(HttpStatusCode.OK, forAdmin.StatusCode);
            Assert.True(forAdmin.Headers.CacheControl?.Private, file);
            Assert.Equal(HttpStatusCode.NotFound, forMember.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, forAnyone.StatusCode);
        }

        // Routing finds static files whatever the case of their address: so does the check.
        // So it does whether the file is asked for compressed or as its source map.
        foreach (var file in new[]
        {
            stylesheet.ToUpperInvariant(), "/dist/ADMIN.css", $"/dist/{adminChunk.ToUpperInvariant()}", "/dist/Chunks.json",
            stylesheet + ".gz", "/dist/admin.css.map", $"/dist/{adminChunk}.gz", $"/dist/{adminChunk}.map.gz", "/dist/chunks.json.gz",
        })
        {
            using var forAnyone = await anonymous.GetAsync(new Uri(file, UriKind.Relative), TestSupport.Ct);
            Assert.True(forAnyone.StatusCode == HttpStatusCode.NotFound, file);
        }

        // Everyone else's chunks are anyone's, and kept for good; the manifest is the server's own.
        using var chunk = await anonymous.GetAsync(new Uri($"/dist/{publicChunk}", UriKind.Relative), TestSupport.Ct);
        using var listed = await asAdmin.GetAsync(new Uri("/dist/chunks.json", UriKind.Relative), TestSupport.Ct);
        Assert.Equal(HttpStatusCode.OK, chunk.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", chunk.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, listed.StatusCode);
    }

    [Fact]
    public async Task APageNamesTheAppsChunksAndThoseOfItsIslandsOnce()
    {
        using var manifest = await ManifestAsync();
        var app = manifest.RootElement.GetProperty("app").EnumerateArray().Select(c => c.GetString()!).ToList();
        var island = manifest.RootElement.GetProperty("islands").GetProperty("public").EnumerateArray().Select(c => c.GetString()!).ToList();
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        var named = ModulePreload().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal([.. app, .. island.Except(app)], named);
        // The island's come just before it.
        var islandAt = html.IndexOf("data-island=\"public\"", StringComparison.Ordinal);
        Assert.True(html.LastIndexOf($"/dist/{island[^1]}", StringComparison.Ordinal) < islandAt);
        Assert.True(html.IndexOf($"/dist/{island[0]}", StringComparison.Ordinal) > html.IndexOf("</head>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAppShellShowsASkeletonShapedLikeThePageUntilItsDataIsDrawn()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var signedOut = WebAuth.NewClient(factory);
        using var signedIn = await WebAuth.SignInAsync(factory, user);

        var drawn = await signedOut.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        using var shellAnswer = await signedIn.GetAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        var shell = await shellAnswer.Content.ReadAsStringAsync(TestSupport.Ct);
        var settings = await signedIn.GetStringAsync(new Uri("/settings/profile", UriKind.Relative), TestSupport.Ct);

        // The shell is the member's own: no shared cache keeps it.
        Assert.True(shellAnswer.Headers.CacheControl?.NoStore);

        // Signed out the server draws the page itself.
        Assert.DoesNotContain("skeleton-page", drawn, StringComparison.Ordinal);
        Assert.Matches("<main id=\"main\" class=\"shell-main\" data-app-shell>\\s*<div class=\"skeleton-page\" data-skeleton=\"feed\" aria-busy=\"true\">\\s*<p class=\"visually-hidden\" role=\"status\">Loading…</p>", shell);
        Assert.Contains("data-skeleton=\"form\"", settings, StringComparison.Ordinal);
        // The layout's small islands have none.
        Assert.Single(SkeletonPage().Matches(shell));
    }

    public void Dispose() => factory.Dispose();

    /// <summary>Written by `npm run build`, which runs before the tests (as MapStaticAssets needs the client built anyway).</summary>
    private async Task<JsonDocument> ManifestAsync()
    {
        var file = factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider.GetFileInfo("dist/chunks.json");
        Assert.True(file.Exists, "build the client first (npm run build)");
        using var stream = file.CreateReadStream();
        return await JsonDocument.ParseAsync(stream, cancellationToken: TestSupport.Ct);
    }

    private static List<string> Stylesheets(string html) => [.. Stylesheet().Matches(html).Select(m => m.Groups[1].Value)];

    private static List<string> Prefetched(string html) => [.. Prefetch().Matches(html).Select(m => m.Groups[1].Value)];

    [GeneratedRegex("<link rel=\"stylesheet\" href=\"/dist/([a-z-]+)\\.[a-z0-9]+\\.css\"")]
    private static partial Regex Stylesheet();

    [GeneratedRegex("<link rel=\"prefetch\" href=\"/dist/([a-z-]+)\\.[a-z0-9]+\\.css\"")]
    private static partial Regex Prefetch();

    [GeneratedRegex("<link href=\"/dist/(chunks/[^\"]+)\" rel=\"modulepreload\">")]
    private static partial Regex ModulePreload();

    [GeneratedRegex("/dist/admin\\.[a-z0-9]+\\.css")]
    private static partial Regex AdminStylesheet();

    [GeneratedRegex("class=\"skeleton-page\"")]
    private static partial Regex SkeletonPage();
}
