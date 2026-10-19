namespace Beeps.Features.Accounts;

/// <summary>
/// A Google or GitHub account the human signs in with (PLAN 6.14): at most one of each per account, and each belongs to
/// one account only.
/// </summary>
public sealed class ExternalLogin
{
    public const int ProviderMaxLength = 16;
    public const int KeyMaxLength = 64;
    public const int NameMaxLength = 254;

    public Guid UserId { get; init; }

    /// <summary>One of <see cref="ExternalProviders.All"/>.</summary>
    public required string Provider { get; init; }

    /// <summary>The provider's own id for the account, which never changes (unlike its email or username).</summary>
    public required string ProviderKey { get; init; }

    /// <summary>What the settings show it as: the Google address, the GitHub username.</summary>
    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The providers people can sign in with, by the name in their addresses (<c>/oauth/github</c>).</summary>
public static class ExternalProviders
{
    public const string Google = "google";
    public const string GitHub = "github";

    public static readonly IReadOnlyList<string> All = [Google, GitHub];

    /// <summary>The provider's own name, the same in every language.</summary>
    public static string DisplayName(string provider) => provider switch
    {
        Google => "Google",
        GitHub => "GitHub",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>The provider spelt as one of <see cref="All"/>; null when it is none of them.</summary>
    public static string? Find(string? provider) => All.FirstOrDefault(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Who the provider said signed in, after they agreed to share it.</summary>
/// <param name="Key">The provider's id for the account (<see cref="ExternalLogin.ProviderKey"/>).</param>
/// <param name="Email">Only an address the provider has verified; null without one.</param>
/// <param name="Name">The person's name there, if they gave one.</param>
/// <param name="Username">Their username there (GitHub), the first guess at one here.</param>
public sealed record ExternalIdentity(string Provider, string Key, string? Email, string? Name, string? Username)
{
    /// <summary>How the settings show the account: its username where it has one, otherwise its address.</summary>
    public string Shown => Username ?? Email ?? Key;
}

/// <summary>What signing in with a provider came to.</summary>
/// <param name="Account">Signed in: the account it is linked to (or was just linked to, by its verified address).</param>
/// <param name="SuggestedUsername">Nobody has it yet: a username to offer, as the form for signing up starts.</param>
public sealed record ExternalSignIn(AccountState? Account, string? SuggestedUsername);

/// <summary>A provider linked to the account, as the settings list it.</summary>
public sealed record LinkedLogin(string Provider, string Name);
