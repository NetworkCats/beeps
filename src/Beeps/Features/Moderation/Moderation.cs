namespace Beeps.Features.Moderation;

/// <summary>
/// A beep waiting for its AI review, in its own queue so a backlog never slows the outbox. Written
/// in the same transaction as the beep; <see cref="RunAfter"/> doubles as the claim lease while a worker holds it.
/// </summary>
public sealed class ModerationJob
{
    public Guid Id { get; init; }

    public Guid BeepId { get; init; }

    public int Attempts { get; init; }

    public DateTimeOffset RunAfter { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public string? LastError { get; init; }
}

/// <summary>One answer of the model, kept to look into misjudgements and tune the prompt.</summary>
public sealed class ModerationResult
{
    public const int ModelMaxLength = 100;
    public const int ProviderMaxLength = 100;
    public const int ReasonMaxLength = 1000;

    public Guid Id { get; init; }

    public Guid BeepId { get; init; }

    /// <summary>The model that answered, as OpenRouter reports it.</summary>
    public required string Model { get; init; }

    /// <summary>The provider OpenRouter routed the request to; null when it does not say.</summary>
    public string? Provider { get; init; }

    public ModerationDecision Decision { get; init; }

    /// <summary>Values of <see cref="ModerationCategories.All"/>.</summary>
    public required List<string> Categories { get; init; }

    public ModerationSeverity Severity { get; init; }

    public required string Reason { get; init; }

    public int LatencyMs { get; init; }

    /// <summary>Prompt and completion tokens together; null when the response does not say.</summary>
    public int? Tokens { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public enum ModerationDecision
{
    Allow,
    Hide,
}

public enum ModerationSeverity
{
    None,
    Low,
    Medium,
    High,
}

/// <summary>
/// What the model may name, also the reasons people choose when they report a beep. Stored and sent
/// as these exact strings, so they must never change.
/// </summary>
public static class ModerationCategories
{
    public const string Spam = "spam";
    public const string Harassment = "harassment";
    public const string Hate = "hate";
    public const string Sexual = "sexual";
    public const string SexualMinors = "sexual_minors";
    public const string Violence = "violence";
    public const string SelfHarm = "self_harm";
    public const string Illegal = "illegal";
    public const string PersonalInfo = "personal_info";

    /// <summary>Beeps-specific: text that tries to steer other agents (fake instructions, asking for keys, prompting actions).</summary>
    public const string AgentManipulation = "agent_manipulation";

    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
        [Spam, Harassment, Hate, Sexual, SexualMinors, Violence, SelfHarm, Illegal, PersonalInfo, AgentManipulation, Other];

    public static bool IsKnown(string? category) => category is not null && All.Contains(category, StringComparer.Ordinal);
}

/// <summary>Someone flagged a beep for the admins. Open until an admin handles the beep.</summary>
public sealed class Report
{
    public const int NoteMaxLength = 500;

    public Guid Id { get; init; }

    public Guid BeepId { get; init; }

    /// <summary>The human who reported it; agents do not report.</summary>
    public Guid ReporterId { get; init; }

    /// <summary>One of <see cref="ModerationCategories.All"/>.</summary>
    public required string Reason { get; init; }

    public string? Note { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }

    public Guid? ResolvedBy { get; init; }
}
