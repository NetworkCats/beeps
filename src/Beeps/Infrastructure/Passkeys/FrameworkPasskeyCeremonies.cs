using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beeps.Features.Accounts;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Passkeys;

/// <summary>
/// The WebAuthn ceremonies with the framework's own passkey handler (ASP.NET Core Identity's
/// <see cref="PasskeyHandler{TUser}"/>, without the rest of Identity). The handler looks the account and its
/// passkeys up through a <see cref="UserManager{TUser}"/>; the one each ceremony gets knows only what the caller handed
/// in (<see cref="KnownPasskeys"/>). The site is the relying party: its host is the RP ID, and its origin the only one
/// an answer may come from. Attestation is neither asked for nor checked: any authenticator will do.
/// </summary>
public sealed partial class FrameworkPasskeyCeremonies(IOptions<SiteOptions> site, ILogger<FrameworkPasskeyCeremonies> logger) : IPasskeyCeremonies
{
    private readonly IOptions<IdentityPasskeyOptions> options = Options.Create(new IdentityPasskeyOptions
    {
        ServerDomain = site.Value.BaseUri.Host,
        // Kept on the authenticator, which can then offer it on the login page without being told whose it is.
        ResidentKeyRequirement = "required",
        // A fingerprint, face or screen lock each time: the passkey alone is enough to sign in.
        UserVerificationRequirement = "required",
        ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin && IsSiteOrigin(context.Origin, site.Value.BaseUri)),
    });

    public async Task<PasskeyChallenge> BeginRegistrationAsync(Guid userId, string name, string displayName, IReadOnlyList<Passkey> existing)
    {
        using var users = Users(userId, existing);
        var entity = new PasskeyUserEntity { Id = Id(userId), Name = name, DisplayName = displayName };
        var result = await new PasskeyHandler<Owner>(users, options).MakeCreationOptionsAsync(entity, Request());
        return new PasskeyChallenge(result.CreationOptionsJson, result.AttestationState ?? "");
    }

    public async Task<NewPasskey?> FinishRegistrationAsync(string credentialJson, string state, CancellationToken cancellationToken)
    {
        // The account it is for is in the state; nothing here is looked up.
        using var users = Users(null, []);
        var result = await new PasskeyHandler<Owner>(users, options).PerformAttestationAsync(new PasskeyAttestationContext
        {
            HttpContext = Request(),
            CredentialJson = WithExtensionResults(credentialJson),
            AttestationState = state,
        });
        if (!result.Succeeded)
        {
            LogRefused(logger, "registration", result.Failure.Message);
            return null;
        }

        var passkey = result.Passkey;
        return new NewPasskey(passkey.CredentialId, passkey.PublicKey, passkey.SignCount, passkey.Transports, passkey.IsBackupEligible, passkey.IsBackedUp);
    }

    public async Task<PasskeyChallenge> BeginAssertionAsync(Guid? userId, IReadOnlyList<Passkey> allowed)
    {
        using var users = Users(userId, allowed);
        var owner = userId is { } id ? new Owner(Id(id)) : null;
        var result = await new PasskeyHandler<Owner>(users, options).MakeRequestOptionsAsync(owner, Request());
        return new PasskeyChallenge(result.RequestOptionsJson, result.AssertionState ?? "");
    }

    public byte[]? CredentialIdOf(string credentialJson)
    {
        try
        {
            using var credential = JsonDocument.Parse(credentialJson);
            return credential.RootElement.ValueKind == JsonValueKind.Object
                && credential.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && Base64Url.DecodeFromChars(id.GetString()) is { Length: > 0 and <= Passkey.CredentialIdMaxLength } bytes
                    ? bytes
                    : null;
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return null;
        }
    }

    public async Task<PasskeyUse?> FinishAssertionAsync(string credentialJson, string state, Passkey passkey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passkey);
        // The handler finds the account by the id the state has, or the one the authenticator gave (its user handle),
        // and then the passkey on it: only the passkey's own account and the passkey itself are there to be found.
        using var users = Users(passkey.UserId, [passkey]);
        var result = await new PasskeyHandler<Owner>(users, options).PerformAssertionAsync(new PasskeyAssertionContext
        {
            HttpContext = Request(),
            CredentialJson = WithExtensionResults(credentialJson),
            AssertionState = state,
        });
        if (!result.Succeeded)
        {
            LogRefused(logger, "sign-in", result.Failure.Message);
            return null;
        }

        return new PasskeyUse(result.Passkey.SignCount, result.Passkey.IsBackedUp);
    }

    /// <summary>
    /// The credential with <c>clientExtensionResults</c>, which the handler requires but nothing here uses: some password
    /// managers, answering in the browser's place, leave it out of their JSON. Anything that is no JSON object is left as
    /// it is, for the handler to refuse.
    /// </summary>
    private static string WithExtensionResults(string credentialJson)
    {
        try
        {
            if (JsonNode.Parse(credentialJson) is JsonObject credential && credential["clientExtensionResults"] is not JsonObject)
            {
                credential["clientExtensionResults"] = new JsonObject();
                return credential.ToJsonString();
            }
        }
        catch (JsonException)
        {
        }

        return credentialJson;
    }

    /// <summary>The account's id as the authenticator keeps it (its user handle), and as the handler looks it up.</summary>
    private static string Id(Guid userId) => userId.ToString("D");

    private static bool IsSiteOrigin(string origin, Uri site) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && string.Equals(uri.GetLeftPart(UriPartial.Authority), site.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The handler takes a request only to read the RP ID and the origin from, which come from the site's address here
    /// (<see cref="IdentityPasskeyOptions.ServerDomain"/>, <see cref="IdentityPasskeyOptions.ValidateOrigin"/>).
    /// </summary>
    private static DefaultHttpContext Request() => new();

    private static UserManager<Owner> Users(Guid? userId, IReadOnlyList<Passkey> passkeys) => new(
        new KnownPasskeys(userId is { } id ? new Owner(Id(id)) : null, passkeys),
        Options.Create(new IdentityOptions()),
        new PasswordHasher<Owner>(),
        [],
        [],
        new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(),
        null!,
        NullLogger<UserManager<Owner>>.Instance);

    [LoggerMessage(Level = LogLevel.Information, Message = "A passkey {Ceremony} was refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string ceremony, string reason);

    /// <summary>An account, as the handler knows it: by its id.</summary>
    private sealed record Owner(string Id);

    /// <summary>
    /// What one ceremony can look up: one account and the passkeys handed in for it. A new passkey's id is checked
    /// against everyone's by the caller (and the database's unique index), so none is found here.
    /// </summary>
    private sealed class KnownPasskeys(Owner? owner, IReadOnlyList<Passkey> passkeys) : IUserPasskeyStore<Owner>
    {
        public Task<Owner?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(owner is not null && string.Equals(owner.Id, userId, StringComparison.Ordinal) ? owner : null);

        public Task<string> GetUserIdAsync(Owner user, CancellationToken cancellationToken) => Task.FromResult(user.Id);

        public Task<IList<UserPasskeyInfo>> GetPasskeysAsync(Owner user, CancellationToken cancellationToken) =>
            Task.FromResult<IList<UserPasskeyInfo>>([.. passkeys.Select(Info)]);

        public Task<UserPasskeyInfo?> FindPasskeyAsync(Owner user, byte[] credentialId, CancellationToken cancellationToken) =>
            Task.FromResult(passkeys.FirstOrDefault(p => p.CredentialId.AsSpan().SequenceEqual(credentialId)) is { } passkey ? Info(passkey) : null);

        public Task<Owner?> FindByPasskeyIdAsync(byte[] credentialId, CancellationToken cancellationToken) => Task.FromResult<Owner?>(null);

        public Task AddOrUpdatePasskeyAsync(Owner user, UserPasskeyInfo passkey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemovePasskeyAsync(Owner user, byte[] credentialId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetUserNameAsync(Owner user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetUserNameAsync(Owner user, string? userName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> GetNormalizedUserNameAsync(Owner user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetNormalizedUserNameAsync(Owner user, string? normalizedName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> CreateAsync(Owner user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> UpdateAsync(Owner user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IdentityResult> DeleteAsync(Owner user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Owner?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Dispose()
        {
        }

        private static UserPasskeyInfo Info(Passkey passkey) => new(
            passkey.CredentialId,
            passkey.PublicKey,
            passkey.CreatedAt,
            (uint)passkey.SignCount,
            passkey.Transports,
            isUserVerified: true,
            passkey.BackupEligible,
            passkey.BackedUp,
            attestationObject: [],
            clientDataJson: []);
    }
}
