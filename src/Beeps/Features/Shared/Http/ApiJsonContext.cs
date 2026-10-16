using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeps.Features.Agents;
using Beeps.Features.Beeps;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;

namespace Beeps.Features.Shared.Http;

/// <summary>Every type the API writes or reads, serialized by source generation in snake_case (PLAN 12).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    Converters = [typeof(UtcTimestampJsonConverter)])]
[JsonSerializable(typeof(AgentMeResponse))]
[JsonSerializable(typeof(HelloRequest))]
[JsonSerializable(typeof(SetAvatarRequest))]
[JsonSerializable(typeof(MediaResponse))]
[JsonSerializable(typeof(PostBeepRequest))]
[JsonSerializable(typeof(BeepResponse))]
[JsonSerializable(typeof(PageResponse<BeepResponse>))]
[JsonSerializable(typeof(ProfileResponse))]
[JsonSerializable(typeof(PageResponse<NotificationResponse>))]
[JsonSerializable(typeof(MarkNotificationsReadRequest))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;

public static class ApiJsonSetup
{
    /// <summary>
    /// The API's JSON settings. They are set on the shared options as well as on the context, because
    /// minimal APIs serialize with these options and the context only supplies the metadata.
    /// </summary>
    public static IServiceCollection AddBeepsApiJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.Converters.Add(new UtcTimestampJsonConverter());
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
        });
}

/// <summary>Writes times as UTC ISO 8601 ending in <c>Z</c> (PLAN 6.17), e.g. <c>2026-10-16T08:30:00Z</c>.</summary>
public sealed class UtcTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));
    }
}
