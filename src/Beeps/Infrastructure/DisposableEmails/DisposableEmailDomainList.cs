using Beeps.Features.Accounts;

namespace Beeps.Infrastructure.DisposableEmails;

/// <summary>Answers from the copy of the list <see cref="DisposableEmailUpdater"/> loaded last; a newer one replaces it whole.</summary>
public sealed class DisposableEmailDomainList : IDisposableEmailDomains
{
    private IReadOnlySet<string>? domains;

    /// <summary>Whether a copy is loaded.</summary>
    public bool IsLoaded => Volatile.Read(ref domains) is not null;

    /// <summary>Starts answering from <paramref name="next"/>: lower-case domains, never changed after.</summary>
    public void Use(IReadOnlySet<string> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Volatile.Write(ref domains, next);
    }

    public bool IsDisposable(string normalizedEmail)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmail);
        if (Volatile.Read(ref domains) is not { } current)
        {
            return false;
        }

        // The services hand out subdomains as readily as the domain itself; a bare top-level domain is never listed.
        var host = normalizedEmail[(normalizedEmail.LastIndexOf('@') + 1)..];
        while (host.Contains('.', StringComparison.Ordinal))
        {
            if (current.Contains(host))
            {
                return true;
            }

            host = host[(host.IndexOf('.', StringComparison.Ordinal) + 1)..];
        }

        return false;
    }
}
