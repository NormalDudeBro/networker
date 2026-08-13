using System.Security.Cryptography;

namespace Networker.Core.Updates;

/// <summary>
/// The SHA-256 checksum sidecar contract: one ASCII line
/// "<c>{64 lowercase hex characters}  {exact msix filename}</c>".
/// </summary>
public static class UpdateChecksum
{
    /// <summary>Upper bound for the sidecar file, in bytes.</summary>
    public const int MaxSidecarBytes = 4096;

    /// <summary>Upper bound for a download package, in bytes (1 GiB).</summary>
    public const long MaxPackageBytes = 1024L * 1024 * 1024;

    /// <summary>
    /// Parses the sidecar content and requires exactly one non-empty line
    /// naming the expected file with a 64-character lowercase hex digest.
    /// </summary>
    public static bool TryParseSidecar(string content, string expectedFileName, out string sha256Hex)
    {
        sha256Hex = string.Empty;
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? match = null;
        foreach (string line in lines)
        {
            string trimmed = line.Trim(' ', '\t', '\r');
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (match is not null)
            {
                return false; // more than one non-empty line
            }

            match = trimmed;
        }

        if (match is null)
        {
            return false;
        }

        // Exactly 64 hex characters followed by two spaces and the file name.
        if (match.Length < 66 || match[64] != ' ' || match[65] != ' ')
        {
            return false;
        }

        string digest = match[..64];
        string name = match[66..];
        if (!IsLowercaseHex(digest) || !string.Equals(name, expectedFileName, StringComparison.Ordinal))
        {
            return false;
        }

        sha256Hex = digest;
        return true;
    }

    /// <summary>
    /// Constant-time comparison of the expected hex digest against a computed
    /// hash. Malformed hex never matches.
    /// </summary>
    public static bool DigestMatches(string sha256Hex, ReadOnlySpan<byte> computed)
    {
        if (sha256Hex.Length != 64 || computed.Length != 32)
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(sha256Hex);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, computed);
    }

    /// <summary>
    /// The digest string handed to <c>AddPackageOptions.ExpectedDigests</c>.
    /// </summary>
    public static string FormatExpectedDigest(string sha256Hex) => "sha256:" + sha256Hex;

    private static bool IsLowercaseHex(string value)
    {
        foreach (char c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
