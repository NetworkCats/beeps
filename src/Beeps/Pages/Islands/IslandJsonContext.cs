using System.Text.Json.Serialization;
using Beeps.Features.Shared.Http;

namespace Beeps.Pages.Islands;

/// <summary>Every type the islands and the app's config are drawn from, serialized by source generation (PLAN 12), in camelCase for the browser.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.Unspecified,
    Converters = [typeof(UtcTimestampJsonConverter)])]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(ClientConfigProps))]
[JsonSerializable(typeof(LiveItems<BeepProps>))]
[JsonSerializable(typeof(LiveItems<NotificationProps>))]
[JsonSerializable(typeof(AccountMenuProps))]
[JsonSerializable(typeof(CaptchaProps))]
[JsonSerializable(typeof(SiteOptionsProps))]
[JsonSerializable(typeof(ToastsProps))]
[JsonSerializable(typeof(IndexProps))]
[JsonSerializable(typeof(PublicProps))]
[JsonSerializable(typeof(TagProps))]
[JsonSerializable(typeof(ThreadProps))]
[JsonSerializable(typeof(ProfileProps))]
[JsonSerializable(typeof(NotificationsProps))]
[JsonSerializable(typeof(ComposeProps))]
[JsonSerializable(typeof(ReportProps))]
[JsonSerializable(typeof(SearchProps))]
[JsonSerializable(typeof(SettingsProps))]
[JsonSerializable(typeof(AgentsProps))]
[JsonSerializable(typeof(AgentEditProps))]
[JsonSerializable(typeof(OnboardingAgentProps))]
[JsonSerializable(typeof(ConnectProps))]
[JsonSerializable(typeof(VerifyProps))]
[JsonSerializable(typeof(AdminOverviewProps))]
[JsonSerializable(typeof(AdminUsersProps))]
[JsonSerializable(typeof(AdminUserDetailsProps))]
[JsonSerializable(typeof(AdminAgentsProps))]
[JsonSerializable(typeof(AdminReviewProps))]
[JsonSerializable(typeof(AdminReviewBeepProps))]
[JsonSerializable(typeof(AdminSystemProps))]
[JsonSerializable(typeof(AdminSettingsProps))]
[JsonSerializable(typeof(AdminAuditProps))]
[JsonSerializable(typeof(ClientViewer))]
internal sealed partial class IslandJsonContext : JsonSerializerContext;
