using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beeps.Tests.Support;

/// <summary>
/// A passkey authenticator in software, as a browser and a phone together would be: it answers the options the site
/// hands <c>navigator.credentials</c> with WebAuthn's JSON, signed with ES256 keys of its own. Each passkey it makes is
/// kept, so it can sign in with it later. Attestation is always "none", as passkey providers give it.
/// </summary>
internal sealed class TestAuthenticator : IDisposable
{
    private const byte UserPresent = 0x01;
    private const byte UserVerified = 0x04;
    private const byte BackupEligible = 0x08;
    private const byte BackedUp = 0x10;
    private const byte AttestedCredentialData = 0x40;

    private static readonly string[] Transports = ["internal", "hybrid"];

    private readonly List<StoredKey> keys = [];

    /// <param name="origin">The page the browser says the ceremony ran on.</param>
    public TestAuthenticator(string origin = "https://beeps.test") => Origin = origin;

    public string Origin { get; set; }

    /// <summary>What the authenticator says it checked: a fingerprint or a screen lock (on), or only a touch (off).</summary>
    public bool VerifiesUser { get; set; } = true;

    /// <summary>The passkeys made, newest last.</summary>
    public IReadOnlyList<StoredKey> Keys => keys;

    /// <summary>Makes a passkey for the options of <c>navigator.credentials.create</c>; the credential, as JSON.</summary>
    public string Create(string optionsJson)
    {
        var options = JsonNode.Parse(optionsJson)!;
        var rpId = options["rp"]!["id"]!.GetValue<string>();
        var userHandle = Base64Url.DecodeFromChars(options["user"]!["id"]!.GetValue<string>());
        var key = new StoredKey(RandomNumberGenerator.GetBytes(16), ECDsa.Create(ECCurve.NamedCurves.nistP256), rpId, userHandle);
        keys.Add(key);

        var clientData = ClientData("webauthn.create", options["challenge"]!.GetValue<string>());
        var parameters = key.Key.ExportParameters(false);
        var publicKey = Cbor.Map(
            (Cbor.Int(1), Cbor.Int(2)),
            (Cbor.Int(3), Cbor.Int(-7)),
            (Cbor.Int(-1), Cbor.Int(1)),
            (Cbor.Int(-2), Cbor.Bytes(parameters.Q.X!)),
            (Cbor.Int(-3), Cbor.Bytes(parameters.Q.Y!)));
        var credentialData = new byte[16 + 2 + key.Id.Length];
        BinaryPrimitives.WriteUInt16BigEndian(credentialData.AsSpan(16), (ushort)key.Id.Length);
        key.Id.CopyTo(credentialData, 18);
        var authenticatorData = AuthenticatorData(rpId, (byte)(Flags() | AttestedCredentialData), 0, [.. credentialData, .. publicKey]);
        var attestationObject = Cbor.Map(
            (Cbor.Text("fmt"), Cbor.Text("none")),
            (Cbor.Text("attStmt"), Cbor.Map()),
            (Cbor.Text("authData"), Cbor.Bytes(authenticatorData)));
        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(key.Id),
            rawId = Base64Url.EncodeToString(key.Id),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientData),
                attestationObject = Base64Url.EncodeToString(attestationObject),
                transports = Transports,
            },
        });
    }

    /// <summary>
    /// Signs with a passkey for the options of <c>navigator.credentials.get</c>: the one the options allow, or the newest
    /// for the site when they allow any (the person picks it); the credential, as JSON.
    /// </summary>
    public string Get(string optionsJson, StoredKey? key = null)
    {
        var options = JsonNode.Parse(optionsJson)!;
        var rpId = options["rpId"]!.GetValue<string>();
        var allowed = options["allowCredentials"]?.AsArray().Select(c => c!["id"]!.GetValue<string>()).ToHashSet() ?? [];
        key ??= keys.LastOrDefault(k => k.RpId == rpId && (allowed.Count == 0 || allowed.Contains(Base64Url.EncodeToString(k.Id))))
            ?? throw new InvalidOperationException("No passkey for these options.");
        key.SignCount++;
        var clientData = ClientData("webauthn.get", options["challenge"]!.GetValue<string>());
        var authenticatorData = AuthenticatorData(rpId, Flags(), key.SignCount, []);
        var signature = key.Key.SignData([.. authenticatorData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(key.Id),
            rawId = Base64Url.EncodeToString(key.Id),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientData),
                authenticatorData = Base64Url.EncodeToString(authenticatorData),
                signature = Base64Url.EncodeToString(signature),
                userHandle = Base64Url.EncodeToString(key.UserHandle),
            },
        });
    }

    public void Dispose()
    {
        foreach (var key in keys)
        {
            key.Key.Dispose();
        }
    }

    private byte Flags() => (byte)(UserPresent | BackupEligible | BackedUp | (VerifiesUser ? UserVerified : 0));

    private byte[] ClientData(string type, string challenge) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin = Origin, crossOrigin = false }));

    private static byte[] AuthenticatorData(string rpId, byte flags, uint signCount, byte[] rest)
    {
        var counter = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(counter, signCount);
        return [.. SHA256.HashData(Encoding.UTF8.GetBytes(rpId)), flags, .. counter, .. rest];
    }

    /// <summary>A passkey the authenticator keeps: its id, its key, and whose it is.</summary>
    internal sealed record StoredKey(byte[] Id, ECDsa Key, string RpId, byte[] UserHandle)
    {
        public uint SignCount { get; set; }
    }

    /// <summary>The little of CBOR an attestation object and a COSE key need.</summary>
    private static class Cbor
    {
        public static byte[] Int(int value) => value >= 0 ? Head(0, (ulong)value) : Head(1, (ulong)(-1 - value));

        public static byte[] Bytes(byte[] value) => [.. Head(2, (ulong)value.Length), .. value];

        public static byte[] Text(string value) => [.. Head(3, (ulong)Encoding.UTF8.GetByteCount(value)), .. Encoding.UTF8.GetBytes(value)];

        public static byte[] Map(params (byte[] Key, byte[] Value)[] entries) =>
            [.. Head(5, (ulong)entries.Length), .. entries.SelectMany(e => e.Key.Concat(e.Value))];

        private static byte[] Head(int major, ulong length) => length switch
        {
            < 24 => [(byte)((major << 5) | (int)length)],
            <= byte.MaxValue => [(byte)((major << 5) | 24), (byte)length],
            <= ushort.MaxValue => [(byte)((major << 5) | 25), (byte)(length >> 8), (byte)length],
            _ => throw new ArgumentOutOfRangeException(nameof(length)),
        };
    }
}
