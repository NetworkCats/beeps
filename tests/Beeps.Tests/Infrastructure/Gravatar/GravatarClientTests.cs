using System.Net;
using Beeps.Features.Avatars;
using Beeps.Features.Media;
using Beeps.Infrastructure.Gravatar;
using Beeps.Tests.Support;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Gravatar;

public sealed class GravatarClientTests
{
    [Fact]
    public async Task AsksForTheTrimmedLowerCaseAddressByItsSha256AtTheAvatarSize()
    {
        using var handler = new StubHttpHandler(HttpStatusCode.OK, [1, 2, 3]);

        var image = await Find(handler, "  MyEmailAddress@Example.com ");

        Assert.Equal([1, 2, 3], image);
        // Gravatar's own example: the SHA-256 of "myemailaddress@example.com".
        Assert.Equal(
            new Uri($"https://gravatar.com/avatar/84059b07d4be67b806386c0aad8070a23f18836bbaae342275dc0a83414c32ee?s={MediaRules.AvatarSide}&d=404&r=g"),
            handler.Request?.RequestUri);
    }

    [Fact]
    public async Task AnAddressWithoutAnImageHasNone()
    {
        using var handler = new StubHttpHandler(HttpStatusCode.NotFound, "");

        Assert.Null(await Find(handler, "nobody@example.com"));
    }

    [Fact]
    public async Task AnImageLargerThanAnUploadIsNotTaken()
    {
        using var handler = new StubHttpHandler(HttpStatusCode.OK, new byte[MediaRules.MaxBytes + 1]);

        Assert.Null(await Find(handler, "big@example.com"));
    }

    [Fact]
    public async Task GravatarAwayThrowsSoTheAddressIsAskedAgain()
    {
        using var failing = new StubHttpHandler(HttpStatusCode.ServiceUnavailable, "");
        using var unreachable = new StubHttpHandler(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => Find(failing, "a@example.com"));
        await Assert.ThrowsAsync<HttpRequestException>(() => Find(unreachable, "a@example.com"));
    }

    private static async Task<byte[]?> Find(StubHttpHandler handler, string email)
    {
        using var http = new HttpClient(handler);
        return await new GravatarClient(http, Options.Create(new GravatarOptions())).FindAsync(email, TestSupport.Ct);
    }
}
