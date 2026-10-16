using System.Net;
using Beeps.Features.Accounts;
using Beeps.Infrastructure.Captcha;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Captcha;

public sealed class TurnstileCaptchaVerifierTests
{
    [Fact]
    public async Task SendsSecretTokenAndIpToSiteverify()
    {
        using var handler = new StubHandler(HttpStatusCode.OK, """{"success":true,"error-codes":[]}""");

        var ok = await Verify(handler, "token-1");

        Assert.True(ok);
        Assert.Equal(TurnstileCaptchaVerifier.SiteVerifyUri, handler.Request?.RequestUri);
        Assert.Equal("secret=s3cret&response=token-1&remoteip=203.0.113.7", handler.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"success":false,"error-codes":["timeout-or-duplicate"]}""")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task AnythingButSuccessIsARejection(HttpStatusCode status, string body)
    {
        using var handler = new StubHandler(status, body);

        Assert.False(await Verify(handler, "token-1"));
    }

    [Fact]
    public async Task UnreachableCloudflareIsARejection()
    {
        using var handler = new StubHandler(new HttpRequestException("down"));

        Assert.False(await Verify(handler, "token-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task MissingTokensAreRejectedWithoutACall(string? token)
    {
        using var handler = new StubHandler(HttpStatusCode.OK, """{"success":true}""");

        Assert.False(await Verify(handler, token));
        Assert.Null(handler.Request);
    }

    private static async Task<bool> Verify(StubHandler handler, string? token)
    {
        using var http = new HttpClient(handler);
        var verifier = new TurnstileCaptchaVerifier(
            http,
            Options.Create(new CaptchaOptions { SiteKey = "site", SecretKey = "s3cret" }),
            NullLogger<TurnstileCaptchaVerifier>.Instance);
        return await verifier.VerifyAsync(token, "203.0.113.7", TestSupport.Ct);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly string body = "";
        private readonly Exception? failure;

        public StubHandler(HttpStatusCode status, string body)
        {
            this.status = status;
            this.body = body;
        }

        public StubHandler(Exception failure) => this.failure = failure;

        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (failure is not null)
            {
                throw failure;
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
