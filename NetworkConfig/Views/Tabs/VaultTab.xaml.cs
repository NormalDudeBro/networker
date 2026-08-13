using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using networker.Models;
using networker.Services;
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
                SetVaultState("No vault exists. Enter a master password to create one.", "AppTextDisabledBrush", unlocked: false);
                VaultUnlockButton.Content = "Create Vault";
                VaultUnlockButton.IsEnabled = true;
                VaultLockButton.IsEnabled = false;
                VaultPasswordInput.IsEnabled = true;
            }
            else if (_vault.IsLocked)
            {
                SetVaultState("Vault locked", "AppWarningBrush", unlocked: false);
                VaultUnlockButton.Content = "Unlock";
                VaultUnlockButton.IsEnabled = true;
                VaultLockButton.IsEnabled = false;
                VaultPasswordInput.IsEnabled = true;
            }
            else
            {
                SetVaultState("Vault unlocked", "AppSuccessBrush", unlocked: true);
                VaultUnlockButton.IsEnabled = false;
                VaultLockButton.IsEnabled = true;
                VaultPasswordInput.IsEnabled = false;
                VaultPasswordInput.Password = string.Empty;
                RefreshVaultLists();
            }
        }

        private void SetVaultState(string message, string brushKey, bool unlocked)
        {
            VaultStatusText.Text = message;
            VaultStatusText.Foreground = (Brush)Application.Current.Resources[brushKey];
            VaultStatusDot.Fill = (Brush)Application.Current.Resources[brushKey];
            VaultDataPanel.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
            VaultLockedState.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;

            if (!unlocked)
            {
                CredentialList.ItemsSource = null;
                VariableList.ItemsSource = null;
                ClearStoredValueInputs();
            }
        }

        private void RefreshVaultLists()
        {
            if (_vault.IsLocked)
            {
                return;
            }

            var credentials = _vault.ListCredentials()
                .Select(name => (Name: name, Info: _vault.GetCredential(name)))
                .Where(item => item.Info is not null)
                .Select(item => new VaultCredentialRow(
                    item.Name,
                    item.Info!.Username,
                    item.Info.Description))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            CredentialList.ItemsSource = credentials;
            CredentialEmptyState.Visibility = credentials.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CredentialList.Visibility = credentials.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            var variables = _vault.GetAllVariables()
                .Select(item => new VaultVariableRow(
                    item.Key,
                    item.Value.IsSecret ? "********" : item.Value.Value,
                    item.Value.IsSecret ? "Secret" : "Normal"))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            VariableList.ItemsSource = variables;
            VariableEmptyState.Visibility = variables.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            VariableList.Visibility = variables.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
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
                    LogActivity("Vault Unlocked", "Master-password vault unlocked", "\uE785");
                }
                else
                {
                    _vault.Create(password);
                    SetStatus("Vault created successfully");
                    LogActivity("Vault Created", "A new encrypted vault was created", "\uE72E");
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
            ClearStoredValueInputs();
            UpdateVaultUi();
            SetStatus("Vault locked");
            LogActivity("Vault Locked", "Vault locked — stored data is encrypted", "\uE72E");
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
                LogActivity("Vault Credential", $"'{name}' stored", "\uE774");
                CredNameInput.Text = string.Empty;
                CredUserInput.Text = string.Empty;
                CredPassInput.Password = string.Empty;
                CredDescInput.Text = string.Empty;
                RefreshVaultLists();
                CredNameInput.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to store credential: {ex.Message}", error: true);
            }
        }

        private async void DeleteCredentialRow_Click(object sender, RoutedEventArgs e)
        {
            if (_vault.IsLocked)
            {
                SetStatus("Vault is locked", error: true);
                return;
            }

            if (sender is not FrameworkElement { DataContext: VaultCredentialRow row }) return;

            var dialog = new ContentDialog
            {
                Title = "Delete credential?",
                Content = $"Delete '{row.Name}' from the encrypted vault? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            if (_vault.DeleteCredential(row.Name))
            {
                SetStatus($"Credential '{row.Name}' deleted");
                LogActivity("Vault Credential", $"'{row.Name}' deleted", "\uE774");
                RefreshVaultLists();
                AddCredentialButton.Focus(FocusState.Programmatic);
            }
            else
            {
                SetStatus($"Credential '{row.Name}' not found", error: true);
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
                LogActivity("Vault Variable", $"'{name}' stored as {(isSecret ? "secret" : "plain")}", "\uE774");
                VarNameInput.Text = string.Empty;
                VarValueInput.Text = string.Empty;
                VarSecretCombo.SelectedIndex = 0;
                RefreshVaultLists();
                VarNameInput.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to store variable: {ex.Message}", error: true);
            }
        }

        private async void DeleteVariableRow_Click(object sender, RoutedEventArgs e)
        {
            if (_vault.IsLocked)
            {
                SetStatus("Vault is locked", error: true);
                return;
            }

            if (sender is not FrameworkElement { DataContext: VaultVariableRow row }) return;

            var dialog = new ContentDialog
            {
                Title = "Delete variable?",
                Content = $"Delete '{row.Name}' from the encrypted vault? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            if (_vault.DeleteVariable(row.Name))
            {
                SetStatus($"Variable '{row.Name}' deleted");
                LogActivity("Vault Variable", $"'{row.Name}' deleted", "\uE774");
                RefreshVaultLists();
                AddVariableButton.Focus(FocusState.Programmatic);
            }
            else
            {
                SetStatus($"Variable '{row.Name}' not found", error: true);
            }
        }

        private void ClearStoredValueInputs()
        {
            CredNameInput.Text = string.Empty;
            CredUserInput.Text = string.Empty;
            CredPassInput.Password = string.Empty;
            CredDescInput.Text = string.Empty;
            VarNameInput.Text = string.Empty;
            VarValueInput.Text = string.Empty;
            VarSecretCombo.SelectedIndex = 0;
        }

        private void SetStatus(string message, bool error = false)
        {
            StatusText.Text = message;
            StatusText.Style = (Style)Application.Current.Resources[
                error ? "InlineErrorTextStyle" : "InlineStatusTextStyle"];
        }

        private static void LogActivity(string title, string detail, string glyph = "\uE774")
        {
            string text = (detail ?? "").Trim();
            RecentActivity.Add(new ActivityItem
            {
                Title = title,
                Detail = text.Length <= 200 ? text : text[..200] + "…",
                Timestamp = DateTime.Now,
                Glyph = glyph,
            });
        }
    }

    public sealed record VaultCredentialRow(string Name, string Username, string Description);

    public sealed record VaultVariableRow(string Name, string DisplayValue, string Classification);
}
