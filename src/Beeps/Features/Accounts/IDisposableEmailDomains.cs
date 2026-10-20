namespace Beeps.Features.Accounts;

/// <summary>
/// Throwaway mailboxes, which a new account cannot use (PLAN 6.13): the disposable-email-domains list, which each
/// instance keeps a copy of and refreshes weekly (<c>Infrastructure/DisposableEmails</c>). Answers from memory; while no
/// copy is loaded, no address is disposable.
/// </summary>
public interface IDisposableEmailDomains
{
    /// <summary>Whether the address is at a listed domain or under one (<c>x.mailinator.com</c> as well as <c>mailinator.com</c>).</summary>
    bool IsDisposable(string normalizedEmail);
}
