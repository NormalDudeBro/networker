using Networker.Core.Services.NetworkConfig;

namespace Networker.Core.Tests.Services.NetworkConfig;

/// <summary>
/// Tests for <see cref="VaultService"/>, ported from NetworkConfigPro
/// tests for <c>src/security/vault.py</c> (SecureVault). The C# vault uses
/// PBKDF2-SHA256 + AES-256-GCM (native .NET, no Fernet compatibility), so
/// these are behavioral rather than byte-format tests.
/// </summary>
public class VaultServiceTests : IDisposable
{
    // Low iteration count keeps the suite fast; the production default is
    // exercised by Create_UsesProductionIterationCount.
    private const int FastIterations = 1_000;

    private readonly List<string> _paths = new();

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover temp file is harmless.
            }
        }
    }

    private VaultService CreateVault(int iterations = FastIterations)
    {
        var path = Path.Combine(Path.GetTempPath(), "networker-vault-tests", $"{Guid.NewGuid():N}.dat");
        _paths.Add(path);
        return new VaultService(path, iterations);
    }

    // ----- Create / Unlock / Lock -----

    [Fact]
    public void Create_CreatesFileAndUnlocks()
    {
        var vault = CreateVault();

        var created = vault.Create("MasterPassword1!");

        Assert.True(created);
        Assert.True(vault.Exists);
        Assert.False(vault.IsLocked);
    }

    [Fact]
    public void Create_ShortPassword_Throws()
    {
        var vault = CreateVault();

        Assert.Throws<ArgumentException>(() => vault.Create("short"));
        Assert.False(vault.Exists);
    }

    [Fact]
    public void Create_EmptyPassword_Throws()
    {
        var vault = CreateVault();

        Assert.Throws<ArgumentException>(() => vault.Create(string.Empty));
        Assert.False(vault.Exists);
    }

    [Fact]
    public void Create_WhenVaultExists_Throws()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        Assert.Throws<InvalidOperationException>(() => vault.Create("AnotherPassword1!"));
    }

    [Fact]
    public void Unlock_MissingVault_Throws()
    {
        var vault = CreateVault();

        Assert.Throws<InvalidOperationException>(() => vault.Unlock("MasterPassword1!"));
    }

    [Fact]
    public void Unlock_CorrectPassword_ReturnsTrue()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        vault.Lock();
        Assert.True(vault.Unlock("MasterPassword1!"));
        Assert.False(vault.IsLocked);
    }

    [Fact]
    public void Unlock_WrongPassword_ReturnsFalse()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        vault.Lock();
        Assert.False(vault.Unlock("WrongPassword1!"));
        Assert.True(vault.IsLocked);
    }

    [Fact]
    public void Unlock_WrongPasswordThenCorrect_DataRemainsIntact()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");
        vault.StoreCredential("admin", "root", "s3cret");

        vault.Lock();
        Assert.False(vault.Unlock("WrongPassword1!"));
        Assert.True(vault.Unlock("MasterPassword1!"));

        var credential = vault.GetCredential("admin");
        Assert.NotNull(credential);
        Assert.Equal("root", credential.Username);
        Assert.Equal("s3cret", credential.Password);
    }

    [Fact]
    public void Unlock_TruncatedFile_ReturnsFalse()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        // Overwrite with garbage that is too short to be a valid vault.
        File.WriteAllBytes(_paths[^1], new byte[] { 1, 2, 3 });

        Assert.False(vault.Unlock("MasterPassword1!"));
    }

    [Fact]
    public void Unlock_GarbageFile_ReturnsFalse()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        File.WriteAllBytes(_paths[^1], new byte[256]); // Right size, wrong content.

        Assert.False(vault.Unlock("MasterPassword1!"));
        Assert.True(vault.IsLocked);
    }

    [Fact]
    public void Lock_ClearsAllState()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");
        vault.StoreCredential("admin", "root", "s3cret");

        vault.Lock();

        Assert.True(vault.IsLocked);
        Assert.Throws<InvalidOperationException>(() => vault.GetCredential("admin"));
    }

    [Fact]
    public void Operations_WhenLocked_Throw()
    {
        var vault = CreateVault();

        Assert.Throws<InvalidOperationException>(() => vault.StoreCredential("a", "b", "c"));
        Assert.Throws<InvalidOperationException>(() => vault.GetCredential("a"));
        Assert.Throws<InvalidOperationException>(() => vault.StoreVariable("v", "x"));
        Assert.Throws<InvalidOperationException>(() => vault.GetAllVariables());
        Assert.Throws<InvalidOperationException>(() => vault.StoreTemplate("t", "{}", "Cisco IOS"));
        Assert.Throws<InvalidOperationException>(() => vault.ListTemplates());
        Assert.Throws<InvalidOperationException>(() => vault.ExportNonSensitive());
    }

    [Fact]
    public void Create_UsesProductionIterationCount()
    {
        Assert.Equal(480_000, VaultService.DefaultIterations);
    }

    // ----- Credentials -----

    [Fact]
    public void StoreAndGetCredential_RoundTrips()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        vault.StoreCredential("core-router", "admin", "p@ssw0rd", "Core router admin");

        var credential = vault.GetCredential("core-router");
        Assert.NotNull(credential);
        Assert.Equal("admin", credential.Username);
        Assert.Equal("p@ssw0rd", credential.Password);
        Assert.Equal("Core router admin", credential.Description);
    }

    [Fact]
    public void StoreCredential_OverwritesExisting()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");
        vault.StoreCredential("core-router", "admin", "old-password");

        vault.StoreCredential("core-router", "admin", "new-password");

        Assert.Equal("new-password", vault.GetCredential("core-router")!.Password);
    }

    [Fact]
    public void GetCredential_Missing_ReturnsNull()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        Assert.Null(vault.GetCredential("missing"));
    }

    [Fact]
    public void DeleteCredential_RemovesAndReturnsTrue()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");
        vault.StoreCredential("core-router", "admin", "p@ssw0rd");

        Assert.True(vault.DeleteCredential("core-router"));
        Assert.Null(vault.GetCredential("core-router"));
    }

    [Fact]
    public void DeleteCredential_Missing_ReturnsFalse()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        Assert.False(vault.DeleteCredential("missing"));
    }

    [Fact]
    public void ListCredentials_ReturnsStoredNames()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");
        vault.StoreCredential("one", "u1", "p1");
        vault.StoreCredential("two", "u2", "p2");

        var names = vault.ListCredentials();

        Assert.Equal(2, names.Count);
        Assert.Contains("one", names);
        Assert.Contains("two", names);
    }

    // ----- Variables -----

    [Fact]
    public void StoreAndGetVariable_RoundTrips()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        vault.StoreVariable("mgmt_network", "10.0.0.0/24");
        vault.StoreVariable("snmp_community", "public123", isSecret: true);

        var plain = vault.GetVariable("mgmt_network");
        Assert.NotNull(plain);
        Assert.Equal("10.0.0.0/24", plain.Value);
        Assert.False(plain.IsSecret);

        var secret = vault.GetVariable("snmp_community");
        Assert.NotNull(secret);
        Assert.Equal("public123", secret.Value);
        Assert.True(secret.IsSecret);
    }

    [Fact]
    public void GetAllVariables_ReturnsAll()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");
        vault.StoreVariable("one", "1");
        vault.StoreVariable("two", "2");

        var all = vault.GetAllVariables();

        Assert.Equal(2, all.Count);
        Assert.Equal("1", all["one"].Value);
    }

    [Fact]
    public void DeleteVariable_Removes()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");
        vault.StoreVariable("one", "1");

        Assert.True(vault.DeleteVariable("one"));
        Assert.False(vault.DeleteVariable("one"));
        Assert.Empty(vault.GetAllVariables());
    }

    // ----- Templates -----

    [Fact]
    public void StoreAndGetTemplate_RoundTrips()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        vault.StoreTemplate("my-template", "interface {{ name }}", "Cisco IOS");

        var template = vault.GetTemplate("my-template");
        Assert.NotNull(template);
        Assert.Equal("interface {{ name }}", template.Content);
        Assert.Equal("Cisco IOS", template.Vendor);
        Assert.Equal(new[] { "my-template" }, vault.ListTemplates());
    }

    [Fact]
    public void DeleteTemplate_Removes()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");
        vault.StoreTemplate("my-template", "content", "Cisco IOS");

        Assert.True(vault.DeleteTemplate("my-template"));
        Assert.Null(vault.GetTemplate("my-template"));
    }

    // ----- Export -----

    [Fact]
    public void ExportNonSensitive_ExcludesSecretVariables_IncludesTemplates()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");
        vault.StoreVariable("open", "visible");
        vault.StoreVariable("hidden", "classified", isSecret: true);
        vault.StoreTemplate("tpl", "content", "Cisco IOS");

        var export = vault.ExportNonSensitive();

        Assert.Single(export.Variables);
        Assert.Equal("visible", export.Variables["open"].Value);
        Assert.Single(export.Templates);
        Assert.Equal("content", export.Templates["tpl"].Content);
    }

    // ----- Change password -----

    [Fact]
    public void ChangePassword_RewrapsVault_DataPersists()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");
        vault.StoreCredential("admin", "root", "s3cret");

        Assert.True(vault.ChangePassword("MasterPassword1!", "NewMasterPassword2!"));

        // Old password no longer works, new one does, data survived.
        Assert.False(vault.Unlock("MasterPassword1!"));
        Assert.True(vault.Unlock("NewMasterPassword2!"));
        Assert.Equal("s3cret", vault.GetCredential("admin")!.Password);
    }

    [Fact]
    public void ChangePassword_WrongOldPassword_ReturnsFalse()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        Assert.False(vault.ChangePassword("WrongPassword1!", "NewMasterPassword2!"));
    }

    [Fact]
    public void ChangePassword_ShortNewPassword_Throws()
    {
        var vault = CreateVault();
        vault.Create("MasterPassword1!");

        Assert.Throws<ArgumentException>(() => vault.ChangePassword("MasterPassword1!", "short"));
    }

    // ----- Persistence -----

    [Fact]
    public void Vault_PersistsAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), "networker-vault-tests", $"{Guid.NewGuid():N}.dat");
        _paths.Add(path);

        var first = new VaultService(path, FastIterations);
        first.Create("MasterPassword1!");
        first.StoreCredential("admin", "root", "s3cret");
        first.StoreVariable("vlan", "100");
        first.StoreTemplate("tpl", "content", "Cisco IOS");
        first.Lock();

        var second = new VaultService(path, FastIterations);
        Assert.True(second.Exists);
        Assert.True(second.Unlock("MasterPassword1!"));

        Assert.Equal("s3cret", second.GetCredential("admin")!.Password);
        Assert.Equal("100", second.GetVariable("vlan")!.Value);
        Assert.Equal("Cisco IOS", second.GetTemplate("tpl")!.Vendor);
    }
}
