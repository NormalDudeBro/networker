using System.Collections.Generic;

namespace Networker.Core.Services.NetworkConfig;

/// <summary>
/// Secure vault service interface for storing sensitive configuration data.
/// </summary>
public interface IVaultService
{
    /// <summary>
    /// Checks if vault is locked.
    /// </summary>
    bool IsLocked { get; }

    /// <summary>
    /// Checks if vault file exists.
    /// </summary>
    bool Exists { get; }

    /// <summary>
    /// Creates a new vault with the given master password.
    /// </summary>
    /// <param name="masterPassword">Master password for the vault.</param>
    /// <returns>True if vault was created successfully.</returns>
    bool Create(string masterPassword);

    /// <summary>
    /// Unlocks an existing vault.
    /// </summary>
    /// <param name="masterPassword">Master password for the vault.</param>
    /// <returns>True if vault was unlocked successfully.</returns>
    bool Unlock(string masterPassword);

    /// <summary>
    /// Locks the vault, clearing all decrypted data from memory.
    /// </summary>
    void Lock();

    /// <summary>
    /// Changes the vault master password.
    /// </summary>
    /// <param name="oldPassword">Current master password.</param>
    /// <param name="newPassword">New master password.</param>
    /// <returns>True if password was changed successfully.</returns>
    bool ChangePassword(string oldPassword, string newPassword);

    /// <summary>
    /// Stores a credential securely.
    /// </summary>
    /// <param name="name">Unique identifier for the credential.</param>
    /// <param name="username">Username.</param>
    /// <param name="password">Password.</param>
    /// <param name="description">Optional description.</param>
    void StoreCredential(string name, string username, string password, string description = "");

    /// <summary>
    /// Retrieves a credential.
    /// </summary>
    /// <param name="name">Credential identifier.</param>
    /// <returns>Credential dict or null if not found.</returns>
    CredentialInfo? GetCredential(string name);

    /// <summary>
    /// Deletes a credential.
    /// </summary>
    /// <param name="name">Credential identifier.</param>
    /// <returns>True if credential was deleted.</returns>
    bool DeleteCredential(string name);

    /// <summary>
    /// Lists all stored credential names.
    /// </summary>
    IReadOnlyList<string> ListCredentials();

    /// <summary>
    /// Stores a template variable.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="value">Variable value.</param>
    /// <param name="isSecret">Whether this is a secret value.</param>
    void StoreVariable(string name, string value, bool isSecret = false);

    /// <summary>
    /// Retrieves a variable.
    /// </summary>
    /// <param name="name">Variable name.</param>
    VariableInfo? GetVariable(string name);

    /// <summary>
    /// Gets all variables.
    /// </summary>
    IReadOnlyDictionary<string, VariableInfo> GetAllVariables();

    /// <summary>
    /// Deletes a variable.
    /// </summary>
    bool DeleteVariable(string name);

    /// <summary>
    /// Stores a custom template.
    /// </summary>
    void StoreTemplate(string name, string content, string vendor);

    /// <summary>
    /// Retrieves a template.
    /// </summary>
    VaultTemplateInfo? GetTemplate(string name);

    /// <summary>
    /// Lists all stored template names.
    /// </summary>
    IReadOnlyList<string> ListTemplates();

    /// <summary>
    /// Deletes a template.
    /// </summary>
    bool DeleteTemplate(string name);

    /// <summary>
    /// Exports non-sensitive data (for backup/sharing).
    /// </summary>
    VaultExport ExportNonSensitive();
}

/// <summary>
/// Credential information.
/// </summary>
public sealed record CredentialInfo
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Variable information.
/// </summary>
public sealed record VariableInfo
{
    public required string Value { get; init; }
    public bool IsSecret { get; init; }
}

/// <summary>
/// Vault template information.
/// </summary>
public sealed record VaultTemplateInfo
{
    public required string Content { get; init; }
    public required string Vendor { get; init; }
}

/// <summary>
/// Vault export data (non-sensitive).
/// </summary>
public sealed record VaultExport
{
    public required IReadOnlyDictionary<string, VariableInfo> Variables { get; init; }
    public required IReadOnlyDictionary<string, VaultTemplateInfo> Templates { get; init; }
}