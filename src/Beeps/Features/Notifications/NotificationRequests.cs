using System.Text.Json.Serialization;

namespace Beeps.Features.Notifications;

/// <summary>Outbox payload: notify whoever a new beep replies to or mentions (PLAN 4.3, 6.8).</summary>
public sealed record BeepNotificationsRequest(Guid BeepId)
{
    public const string MessageType = "notifications.beep";
}

/// <summary>Outbox payload: notify the author that <see cref="ActorId"/> liked their beep.</summary>
public sealed record LikeNotificationRequest(Guid BeepId, Guid ActorId)
{
    public const string MessageType = "notifications.like";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(BeepNotificationsRequest))]
[JsonSerializable(typeof(LikeNotificationRequest))]
internal sealed partial class NotificationsJsonContext : JsonSerializerContext;
