using System.Net;
using System.Security.Cryptography;
using System.Text;
using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Media;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Gravatar;

/// <summary>
/// Gravatar's image for an address, by the SHA-256 of the address trimmed and in lower case, at the size avatars are
/// stored in. Only images rated for everyone: Gravatar answers 404 for the others as for none.
/// </summary>
public sealed class GravatarClient(HttpClient http, IOptions<GravatarOptions> options) : IGravatar
{
    public async Task<byte[]?> FindAsync(string email, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AccountRules.NormalizeEmail(email))));
        // d=404: nothing rather than a pattern of Gravatar's, which would hide ours.
        var uri = new Uri(options.Value.BaseUri, $"{hash}?s={MediaRules.AvatarSide}&d=404&r=g");
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MediaRules.MaxBytes)
        {
            return null;
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await ReadAsync(body, cancellationToken);
    }

    /// <summary>Reads at most one byte past what an upload may be, so a huge answer is never held in full; null if too large.</summary>
    private static async Task<byte[]?> ReadAsync(Stream source, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MediaRules.MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
