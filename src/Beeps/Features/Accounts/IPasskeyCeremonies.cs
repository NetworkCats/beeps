namespace Beeps.Features.Accounts;

/// <summary>
/// The two WebAuthn ceremonies: making a passkey and signing in with one. Each starts with the options the
/// browser hands the authenticator, and ends with a check of what it answered against the state kept from the start.
/// Knows nothing of the database: what an answer is checked against is given by the caller.
/// </summary>
public interface IPasskeyCeremonies
{
    /// <summary>Options for <c>navigator.credentials.create</c>: a passkey for the account that the browser can find by itself.</summary>
    /// <param name="existing">The account's passkeys, which the authenticator is asked not to make again.</param>
    Task<PasskeyChallenge> BeginRegistrationAsync(Guid userId, string name, string displayName, IReadOnlyList<Passkey> existing);

    /// <summary>The passkey the authenticator made, if its answer holds up; null if it does not.</summary>
    Task<NewPasskey?> FinishRegistrationAsync(string credentialJson, string state, CancellationToken cancellationToken);

    /// <summary>Options for <c>navigator.credentials.get</c>.</summary>
    /// <param name="userId">The account that has to sign; none to let the person pick any passkey they have for the site.</param>
    /// <param name="allowed">That account's passkeys; empty when there is none.</param>
    Task<PasskeyChallenge> BeginAssertionAsync(Guid? userId, IReadOnlyList<Passkey> allowed);

    /// <summary>The id of the passkey an answer to <see cref="BeginAssertionAsync"/> says it signed with, to look it up by; null if it names none.</summary>
    byte[]? CredentialIdOf(string credentialJson);

    /// <summary>
    /// What the authenticator says of the passkey after it signed, if the answer holds up for <paramref name="passkey"/>
    /// (the one <see cref="CredentialIdOf"/> named) and the account the state was started for; null if it does not.
    /// </summary>
    Task<PasskeyUse?> FinishAssertionAsync(string credentialJson, string state, Passkey passkey, CancellationToken cancellationToken);
}
