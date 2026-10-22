using System.Net;

namespace Beeps.Tests.Support;

/// <summary>Answers every request with one response (text or bytes, or a failure), and keeps the last request and its body.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode status;
    private readonly string body = "";
    private readonly byte[]? bytes;
    private readonly Exception? failure;

    public StubHttpHandler(HttpStatusCode status, string body)
    {
        this.status = status;
        this.body = body;
    }

    public StubHttpHandler(HttpStatusCode status, byte[] bytes)
    {
        this.status = status;
        this.bytes = bytes;
    }

    public StubHttpHandler(Exception failure) => this.failure = failure;

    public HttpRequestMessage? Request { get; private set; }

    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Request = request;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (failure is not null)
        {
            throw failure;
        }

        return new HttpResponseMessage(status) { Content = bytes is null ? new StringContent(body) : new ByteArrayContent(bytes) };
    }
}
