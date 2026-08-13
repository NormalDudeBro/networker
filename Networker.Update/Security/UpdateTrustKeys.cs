using System.Reflection;

namespace Networker.Update.Security;

public static class UpdateTrustKeys
{
    public static IReadOnlyDictionary<string, byte[]> PublicKeys { get; } = Load();

    private static IReadOnlyDictionary<string, byte[]> Load()
    {
        Dictionary<string, string> metadata = typeof(UpdateTrustKeys).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(value => value.Key is not null && value.Value is not null)
            .ToDictionary(value => value.Key!, value => value.Value!, StringComparer.Ordinal);
        if (!metadata.TryGetValue("NetworkerUpdateKeyId", out string? keyId)
            || !metadata.TryGetValue("NetworkerUpdatePublicKeyBase64", out string? encoded)
            || string.IsNullOrWhiteSpace(keyId)) return new Dictionary<string, byte[]>();
        try
        {
            byte[] key = Convert.FromBase64String(encoded);
            using var ecdsa = System.Security.Cryptography.ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key, out int read);
            return read == key.Length && ecdsa.KeySize == 256
                ? new Dictionary<string, byte[]>(StringComparer.Ordinal) { [keyId] = key }
                : new Dictionary<string, byte[]>();
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            return new Dictionary<string, byte[]>();
        }
    }
}
