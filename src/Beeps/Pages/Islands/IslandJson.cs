using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Beeps.Pages.Islands;

/// <summary>
/// The JSON islands are drawn from: an island's envelope (<c>{"props": …, "errors": {field: [message]}}</c>, errors
/// only when the form it holds was refused) and the answers of the <c>/live</c> fetches. camelCase, as the browser
/// reads it; written into the page inside <c>&lt;script type="application/json"&gt;</c>, so <c>&lt;</c>, <c>&gt;</c>
/// and <c>&amp;</c> are always escaped (no <c>&lt;/script&gt;</c> can end it early), while other text stays as written.
/// </summary>
public static class IslandJson
{
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

    private static readonly IslandJsonContext Context = new(new JsonSerializerOptions(IslandJsonContext.Default.Options) { Encoder = Encoder });

    public static string Serialize<T>(T value) => Write(writer => JsonSerializer.Serialize(writer, value, typeof(T), Context));

    /// <summary>One island's props, with the errors of a form it shows that was just refused.</summary>
    public static string Envelope(object? props, ModelStateDictionary modelState)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("props");
            if (props is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(writer, props, props.GetType(), Context);
            }

            if (modelState.ErrorCount > 0)
            {
                writer.WriteStartObject("errors");
                foreach (var (field, entry) in modelState)
                {
                    if (entry.Errors.Count == 0)
                    {
                        continue;
                    }

                    writer.WriteStartArray(field);
                    foreach (var error in entry.Errors)
                    {
                        writer.WriteStringValue(error.ErrorMessage);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        });
    }

    /// <summary>What <c>_Islands.cshtml</c> answers: the page's title and notices, its islands by name, and the rest of the page.</summary>
    public static string Page(string title, string? notice, string? alert, IReadOnlyDictionary<string, string> islands, IslandPageProps page)
    {
        ArgumentNullException.ThrowIfNull(islands);
        ArgumentNullException.ThrowIfNull(page);
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("title", title);
            if (notice is not null)
            {
                writer.WriteString("notice", notice);
            }

            if (alert is not null)
            {
                writer.WriteString("alert", alert);
            }

            writer.WriteStartObject("islands");
            foreach (var (name, envelope) in islands)
            {
                writer.WritePropertyName(name);
                writer.WriteRawValue(envelope, skipInputValidation: true);
            }

            writer.WriteEndObject();
            writer.WritePropertyName("page");
            JsonSerializer.Serialize(writer, page, Context.IslandPageProps);

            writer.WriteEndObject();
        });
    }

    private static string Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = Encoder }))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
