using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Secure vault storing sensitive network configuration data (credentials,
/// template variables, custom templates) encrypted at rest.
/// </summary>
/// <remarks>
/// <para>
/// Ported from NetworkConfigPro <c>src/security/vault.py</c> (<c>SecureVault</c>).
/// The Python Fernet format is intentionally NOT compatible — this is a native
/// .NET design: the master password is stretched with PBKDF2-SHA256 and the
/// payload is sealed with AES-256-GCM, mirroring Python's PBKDF2-SHA256 key
/// derivation semantics with a more modern authenticated cipher. Only BCL APIs
/// are used (no NuGet dependency).
/// </para>
/// <para>
/// On-disk layout: <c>salt(16) || nonce(12) || ciphertext || tag(16)</c>. Writes
/// are atomic (temp file + replace). The vault lives under the per-user
/// <c>%LOCALAPPDATA%\Networker</c> directory whose NTFS-inherited ACLs already
/// restrict access to the current user, approximating Python's <c>0o600</c>.
/// </para>
/// </remarks>
public sealed class VaultService : IVaultService
{
    /// <summary>
    /// OWASP-recommended PBKDF2-SHA256 iteration minimum (matches Python's
    /// <c>SecureVault.ITERATIONS</c>).
    /// </summary>
    public const int DefaultIterations = 480_000;

    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int MinPasswordLength = 8;

    private readonly string _vaultPath;
    private readonly int _iterations;

    private byte[]? _key;
    private VaultData _data = new();
    private bool _isUnlocked;

    /// <summary>
    /// Initializes the vault service.
    /// </summary>
    /// <param name="vaultPath">
    /// Path to the vault file. Defaults to <c>%LOCALAPPDATA%\Networker\vault.dat</c>.
    /// </param>
    /// <param name="iterations">
    /// PBKDF2 iteration count. Defaults to <see cref="DefaultIterations"/>.
    /// Tests may lower this to keep the suite fast.
    /// </param>
    public VaultService(string? vaultPath = null, int iterations = DefaultIterations)
    {
        _vaultPath = vaultPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Networker",
            "vault.dat");
        _iterations = iterations;
    }

    /// <inheritdoc />
    public bool IsLocked => !_isUnlocked;

    /// <inheritdoc />
    public bool Exists => File.Exists(_vaultPath);

    /// <inheritdoc />
    public bool Create(string masterPassword)
    {
        if (Exists)
        {
            throw new InvalidOperationException("Vault already exists. Use Unlock() or delete the vault file first.");
        }

        if (masterPassword.Length < MinPasswordLength)
        {
            throw new ArgumentException(
                $"Master password must be at least {MinPasswordLength} characters",
                nameof(masterPassword));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        _key = DeriveKey(masterPassword, salt);
        _data = new VaultData();
        Save(salt);
        _isUnlocked = true;
        return true;
    }

    /// <inheritdoc />
    public bool Unlock(string masterPassword)
    {
        if (!Exists)
        {
            throw new InvalidOperationException("Vault does not exist. Create one first.");
        }

        // Drop any previous session state before attempting a fresh unlock.
        Clear();

        var bytes = File.ReadAllBytes(_vaultPath);
        if (bytes.Length < SaltSize + NonceSize + TagSize)
        {
            return false; // Truncated or corrupt file.
        }

        var salt = bytes.AsSpan(0, SaltSize).ToArray();
        var nonce = bytes.AsSpan(SaltSize, NonceSize).ToArray();
        var tag = bytes.AsSpan(bytes.Length - TagSize, TagSize).ToArray();
        var ciphertext = bytes.AsSpan(SaltSize + NonceSize, bytes.Length - SaltSize - NonceSize - TagSize).ToArray();

        var key = DeriveKey(masterPassword, salt);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(key);
            return false; // Wrong master password.
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(key);
            return false; // Corrupt ciphertext.
        }

        try
        {
            _data = JsonSerializer.Deserialize<VaultData>(plaintext) ?? new VaultData();
        }
        catch (JsonException)
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
            return false; // Decrypted payload is not valid vault JSON.
        }

        _key = key;
        _isUnlocked = true;
        return true;
    }

    /// <inheritdoc />
    public void Lock()
    {
        Clear();
    }

    /// <inheritdoc />
    public bool ChangePassword(string oldPassword, string newPassword)
    {
        if (newPassword.Length < MinPasswordLength)
        {
            throw new ArgumentException(
                $"New password must be at least {MinPasswordLength} characters",
                nameof(newPassword));
        }

        if (!Unlock(oldPassword))
        {
            return false;
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        _key = DeriveKey(newPassword, salt);
        Save(salt);
        return true;
    }

    /// <inheritdoc />
    public void StoreCredential(string name, string username, string password, string description = "")
    {
        EnsureUnlocked();
        _data.Credentials[name] = new CredentialInfo { Username = username, Password = password, Description = description };
        SaveCurrent();
    }

    /// <inheritdoc />
    public CredentialInfo? GetCredential(string name)
    {
        EnsureUnlocked();
        return _data.Credentials.TryGetValue(name, out var credential) ? credential : null;
    }

    /// <inheritdoc />
    public bool DeleteCredential(string name)
    {
        EnsureUnlocked();
        if (_data.Credentials.Remove(name))
        {
            SaveCurrent();
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListCredentials()
    {
        EnsureUnlocked();
        return _data.Credentials.Keys.ToList();
    }

    /// <inheritdoc />
    public void StoreVariable(string name, string value, bool isSecret = false)
    {
        EnsureUnlocked();
        _data.Variables[name] = new VariableInfo { Value = value, IsSecret = isSecret };
        SaveCurrent();
    }

    /// <inheritdoc />
    public VariableInfo? GetVariable(string name)
    {
        EnsureUnlocked();
        return _data.Variables.TryGetValue(name, out var variable) ? variable : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, VariableInfo> GetAllVariables()
    {
        EnsureUnlocked();
        return new Dictionary<string, VariableInfo>(_data.Variables);
    }

    /// <inheritdoc />
    public bool DeleteVariable(string name)
    {
        EnsureUnlocked();
        if (_data.Variables.Remove(name))
        {
            SaveCurrent();
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void StoreTemplate(string name, string content, string vendor)
    {
        EnsureUnlocked();
        _data.Templates[name] = new VaultTemplateInfo { Content = content, Vendor = vendor };
        SaveCurrent();
    }

    /// <inheritdoc />
    public VaultTemplateInfo? GetTemplate(string name)
    {
        EnsureUnlocked();
        return _data.Templates.TryGetValue(name, out var template) ? template : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListTemplates()
    {
        EnsureUnlocked();
        return _data.Templates.Keys.ToList();
    }

    /// <inheritdoc />
    public bool DeleteTemplate(string name)
    {
        EnsureUnlocked();
        if (_data.Templates.Remove(name))
        {
            SaveCurrent();
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public VaultExport ExportNonSensitive()
    {
        EnsureUnlocked();

        var variables = _data.Variables
            .Where(kvp => !kvp.Value.IsSecret)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return new VaultExport
        {
            Variables = variables,
            Templates = new Dictionary<string, VaultTemplateInfo>(_data.Templates),
        };
    }

    private byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, KeySize);

    private void Save(byte[] salt)
    {
        var key = _key ?? throw new InvalidOperationException("Vault is not unlocked.");
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(_data);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_vaultPath)!);

        // Write atomically: temp file, then replace. Never leave a partial vault.
        var tempPath = _vaultPath + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(salt, 0, salt.Length);
                stream.Write(nonce, 0, nonce.Length);
                stream.Write(ciphertext, 0, ciphertext.Length);
                stream.Write(tag, 0, tag.Length);
            }

            File.Move(tempPath, _vaultPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void SaveCurrent()
    {
        if (!Exists)
        {
            throw new InvalidOperationException("Vault is not properly initialized.");
        }

        // Re-read the salt that sealed the current vault so the key stays valid.
        var salt = new byte[SaltSize];
        using (var stream = new FileStream(_vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            stream.ReadExactly(salt, 0, SaltSize);
        }

        Save(salt);
    }

    private void EnsureUnlocked()
    {
        if (IsLocked)
        {
            throw new InvalidOperationException("Vault is locked. Call Unlock() first.");
        }
    }

    private void Clear()
    {
        if (_key is not null)
        {
            CryptographicOperations.ZeroMemory(_key);
        }

        _key = null;
        _data = new VaultData();
        _isUnlocked = false;
    }

    /// <summary>
    /// Serializable in-memory vault payload. Keys mirror Python's
    /// <c>vault.dat</c> top-level structure (credentials/variables/templates).
    /// </summary>
    private sealed class VaultData
    {
        public Dictionary<string, CredentialInfo> Credentials { get; set; } = new();
        public Dictionary<string, VariableInfo> Variables { get; set; } = new();
        public Dictionary<string, VaultTemplateInfo> Templates { get; set; } = new();
    }
}
