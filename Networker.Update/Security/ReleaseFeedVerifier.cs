using System.Security.Cryptography;
using System.Text.Json;

namespace Networker.Update.Security;

public sealed class ReleaseFeedVerifier
{
    private readonly IReadOnlyDictionary<string, byte[]> _publicKeys;

    public ReleaseFeedVerifier(IReadOnlyDictionary<string, byte[]> publicKeys)
    {
        ArgumentNullException.ThrowIfNull(publicKeys);
        _publicKeys = new Dictionary<string, byte[]>(publicKeys, StringComparer.Ordinal);
    }

    public bool Verify(ReadOnlySpan<byte> feed, ReadOnlySpan<byte> signatureDocument, out string keyId)
    {
        keyId = string.Empty;
        try
        {
            SignatureEnvelope? envelope = JsonSerializer.Deserialize<SignatureEnvelope>(signatureDocument);
            if (envelope is null || envelope.Schema != 1 || string.IsNullOrWhiteSpace(envelope.KeyId)
                || string.IsNullOrWhiteSpace(envelope.Signature)
                || !_publicKeys.TryGetValue(envelope.KeyId, out byte[]? key)) return false;

            byte[] signature = Convert.FromBase64String(envelope.Signature);
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key, out int read);
            if (read != key.Length) return false;
            bool valid = ecdsa.VerifyData(feed, signature, HashAlgorithmName.SHA256);
            if (valid) keyId = envelope.KeyId;
            return valid;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException)
        {
            return false;
        }
    }

    public static byte[] CreateSignatureDocument(ReadOnlySpan<byte> feed, ECDsa privateKey, string keyId)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException("Key ID is required.", nameof(keyId));
        byte[] signature = privateKey.SignData(feed, HashAlgorithmName.SHA256);
        return JsonSerializer.SerializeToUtf8Bytes(new SignatureEnvelope(1, keyId, Convert.ToBase64String(signature)));
    }

    private sealed record SignatureEnvelope(int Schema, string KeyId, string Signature);
}
