using Beeps.Pages.Islands;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using System.Text.Encodings.Web;

namespace Beeps.Tests.Pages;

public sealed class ClientChunksTests : IDisposable
{
    private readonly string webRoot = Directory.CreateTempSubdirectory("beeps-chunks").FullName;

    [Fact]
    public void EachChunkIsNamedOncePerResponse()
    {
        var chunks = Create("""
            { "app": ["chunks/preact.js"], "islands": { "thread": ["chunks/Thread.js", "chunks/cards.js"], "profile": ["chunks/Profile.js", "chunks/cards.js"] } }
            """);
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/beeps";

        Assert.Equal("<link href=\"/beeps/dist/chunks/preact.js\" rel=\"modulepreload\">", Html(chunks.ForApp(context)));
        Assert.Equal(
            "<link href=\"/beeps/dist/chunks/Thread.js\" rel=\"modulepreload\"><link href=\"/beeps/dist/chunks/cards.js\" rel=\"modulepreload\">",
            Html(chunks.ForIsland(context, "thread")));
        Assert.Equal("<link href=\"/beeps/dist/chunks/Profile.js\" rel=\"modulepreload\">", Html(chunks.ForIsland(context, "profile")));
        Assert.Equal("", Html(chunks.ForIsland(context, "thread")));
        // Another response names them again.
        Assert.Contains("Thread.js", Html(chunks.ForIsland(new DefaultHttpContext(), "thread")), StringComparison.Ordinal);
    }

    [Fact]
    public void IslandsWithoutChunksAndAClientNotBuiltNameNone()
    {
        var context = new DefaultHttpContext();

        Assert.Equal("", Html(Create("""{ "app": [], "islands": {} }""").ForIsland(context, "toasts")));
        Assert.Equal("", Html(Create(null).ForApp(context)));
        Assert.Equal("", Html(Create("not json").ForApp(context)));
    }

    public void Dispose() => Directory.Delete(webRoot, recursive: true);

    private ClientChunks Create(string? manifest)
    {
        var dist = Directory.CreateDirectory(Path.Combine(webRoot, "dist"));
        var path = Path.Combine(dist.FullName, "chunks.json");
        if (manifest is null)
        {
            File.Delete(path);
        }
        else
        {
            File.WriteAllText(path, manifest);
        }

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        environment.WebRootFileProvider.Returns(new PhysicalFileProvider(webRoot));
        return new ClientChunks(environment);
    }

    private static string Html(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
