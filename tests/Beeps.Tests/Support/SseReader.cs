namespace Beeps.Tests.Support;

/// <summary>One server-sent event as the browser would see it.</summary>
internal sealed record SseEvent(string Type, string Data, string? Id, string? Retry);

/// <summary>Reads server-sent events off a streaming response, like EventSource does.</summary>
internal sealed class SseReader(Stream body) : IDisposable
{
    private readonly StreamReader reader = new(body);

    public async Task<SseEvent> NextAsync(TimeSpan? timeout = null)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
        cancel.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
        string type = "message";
        string? id = null;
        string? retry = null;
        var data = new List<string>();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancel.Token) ?? throw new EndOfStreamException("The stream ended.");
            if (line.Length == 0)
            {
                if (data.Count > 0)
                {
                    return new SseEvent(type, string.Join('\n', data), id, retry);
                }

                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var (field, value) = colon < 0 ? (line, "") : (line[..colon], line[(colon + 1)..].TrimStart(' '));
            switch (field)
            {
                case "event": type = value; break;
                case "data": data.Add(value); break;
                case "id": id = value; break;
                case "retry": retry = value; break;
            }
        }
    }

    public void Dispose() => reader.Dispose();
}
