using System;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Networker.Core.Services.NetworkConfig;

namespace networker.NetworkConfig.Views.Tabs
{
    /// <summary>
    /// Vault tab — encrypted credential and variable storage backed by
    /// <see cref="IVaultService"/>. Ported from NetworkConfigPro's Vault tab
    /// (<c>_update_vault_ui</c>, <c>_vault_unlock_or_create</c>,
    /// <c>_vault_lock</c>, <c>_vault_add_credential</c>,
    /// <c>_vault_delete_credential</c>, <c>_vault_add_variable</c>).
    /// </summary>
    public sealed partial class VaultTab : UserControl
    {
        private readonly IVaultService _vault;

        public VaultTab()
        {
            this.InitializeComponent();

            var services = ((App)Application.Current).Services;
            _vault = services.GetService<IVaultService>()
                ?? throw new InvalidOperationException("IVaultService is not registered in the DI container.");

            UpdateVaultUi();
        }

        /// <summary>
        /// Drives the locked / unlocked / no-vault states, mirroring Python's
        /// <c>_update_vault_ui</c>.
        /// </summary>
        private void UpdateVaultUi()
        {
            if (!_vault.Exists)
            {
                VaultStatusText.Text = "No vault exists - Enter a password to create one";
                VaultUnlockButton.Content = "Create Vault";
                VaultUnlockButton.IsEnabled = true;
                VaultLockButton.IsEnabled = false;
                VaultPasswordInput.IsEnabled = true;
                CredsList.Text = "(No vault)";
                VarsList.Text = "(No vault)";
            }
            else if (_vault.IsLocked)
            {
                VaultStatusText.Text = "Vault is LOCKED";
                VaultUnlockButton.Content = "Unlock";
                VaultUnlockButton.IsEnabled = true;
                VaultLockButton.IsEnabled = false;
                VaultPasswordInput.IsEnabled = true;
                CredsList.Text = "(Vault is locked)";
                VarsList.Text = "(Vault is locked)";
            }
            else
            {
                VaultStatusText.Text = "Vault is UNLOCKED";
                VaultUnlockButton.IsEnabled = false;
                VaultLockButton.IsEnabled = true;
                VaultPasswordInput.IsEnabled = false;
                VaultPasswordInput.Password = string.Empty;
                RefreshVaultLists();
            }
        }

        private void RefreshVaultLists()
        {
            if (_vault.IsLocked)
            {
                return;
            }

            var credentialNames = _vault.ListCredentials();
            if (credentialNames.Count == 0)
            {
                CredsList.Text = "(No credentials stored)";
            }
            else
            {
                var sb = new StringBuilder();
                foreach (var name in credentialNames)
                {
                    var credential = _vault.GetCredential(name);
                    if (credential is not null)
                    {
                        sb.AppendLine($"{name}: {credential.Username} - {credential.Description}");
                    }
                }

                CredsList.Text = sb.ToString();
            }

            var variables = _vault.GetAllVariables();
            if (variables.Count == 0)
            {
                VarsList.Text = "(No variables stored)";
            }
            else
            {
                var sb = new StringBuilder();
                foreach (var (name, variable) in variables)
                {
                    sb.AppendLine(variable.IsSecret ? $"{name}: ******** (secret)" : $"{name}: {variable.Value}");
                }

                VarsList.Text = sb.ToString();
            }
        }

        private void UnlockOrCreate_Click(object sender, RoutedEventArgs e)
        {
            var password = VaultPasswordInput.Password;
            if (password.Length == 0)
            {
                SetStatus("Please enter a master password", error: true);
                return;
            }

            try
            {
                if (_vault.Exists)
                {
                    if (!_vault.Unlock(password))
                    {
                        SetStatus("Vault error: Invalid master password", error: true);
                        return;
                    }

                    SetStatus("Vault unlocked successfully");
                }
                else
                {
                    _vault.Create(password);
                    SetStatus("Vault created successfully");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                SetStatus($"Vault error: {ex.Message}", error: true);
                return;
            }

            UpdateVaultUi();
        }

        private void Lock_Click(object sender, RoutedEventArgs e)
        {
            _vault.Lock();
            UpdateVaultUi();
            SetStatus("Vault locked");
        }

        private void AddCredential_Click(object sender, RoutedEventArgs e)
        {
            if (_vault.IsLocked)
            {
                SetStatus("Vault is locked", error: true);
                return;
            }

            var name = CredNameInput.Text.Trim();
            var username = CredUserInput.Text.Trim();
            var password = CredPassInput.Password;
            var description = CredDescInput.Text.Trim();

            if (name.Length == 0 || username.Length == 0 || password.Length == 0)
            {
                SetStatus("Name, username, and password are required", error: true);
                return;
            }

            try
            {
                _vault.StoreCredential(name, username, password, description);
                SetStatus($"Credential '{name}' stored");
                CredNameInput.Text = string.Empty;
                CredUserInput.Text = string.Empty;
                CredPassInput.Password = string.Empty;
                CredDescInput.Text = string.Empty;
                RefreshVaultLists();
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to store credential: {ex.Message}", error: true);
            }
        }

        private void DeleteCredential_Click(object sender, RoutedEventArgs e)
        {
            if (_vault.IsLocked)
            {
                SetStatus("Vault is locked", error: true);
                return;
            }

            var name = DelCredNameInput.Text.Trim();
            if (name.Length == 0)
            {
                SetStatus("Enter credential name to delete", error: true);
                return;
            }

            if (_vault.DeleteCredential(name))
            {
                SetStatus($"Credential '{name}' deleted");
                DelCredNameInput.Text = string.Empty;
                RefreshVaultLists();
            }
            else
            {
                SetStatus($"Credential '{name}' not found", error: true);
            }
        }

        private void AddVariable_Click(object sender, RoutedEventArgs e)
        {
            if (_vault.IsLocked)
            {
                SetStatus("Vault is locked", error: true);
                return;
            }

            var name = VarNameInput.Text.Trim();
            var value = VarValueInput.Text.Trim();
            var isSecret = VarSecretCombo.SelectedIndex == 1;

            if (name.Length == 0 || value.Length == 0)
            {
                SetStatus("Variable name and value are required", error: true);
                return;
            }

            try
            {
                _vault.StoreVariable(name, value, isSecret);
                SetStatus($"Variable '{name}' stored");
                VarNameInput.Text = string.Empty;
                VarValueInput.Text = string.Empty;
                VarSecretCombo.SelectedIndex = 0;
                RefreshVaultLists();
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to store variable: {ex.Message}", error: true);
            }
        }

        private void SetStatus(string message, bool error = false)
        {
            StatusText.Text = message;
            StatusText.Foreground = error
                ? (Brush)Application.Current.Resources["AppDangerBrush"]
                : (Brush)Application.Current.Resources["AppTextSecondaryBrush"];
        }
    }
}
