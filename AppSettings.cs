using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace networker
{
    public static class AppSettings
    {
        private static readonly SettingsStore LocalSettings = SettingsStore.Create();

        public static string OllamaEndpoint
        {
            get => (LocalSettings.Values["OllamaEndpoint"] as string) ?? "http://localhost:11434";
            set => LocalSettings.Values["OllamaEndpoint"] = value;
        }

        public static string OllamaApiKey
        {
            get => (LocalSettings.Values["OllamaApiKey"] as string) ?? "";
            set => LocalSettings.Values["OllamaApiKey"] = value;
        }

        public static string SelectedModel
        {
            get => IsCodexProvider(SelectedProvider)
                ? (LocalSettings.Values["CodexSelectedModel"] as string) ?? (LocalSettings.Values["SelectedModel"] as string) ?? ""
                : (LocalSettings.Values["SelectedModel"] as string) ?? "";
            set => LocalSettings.Values[IsCodexProvider(SelectedProvider) ? "CodexSelectedModel" : "SelectedModel"] = value;
        }

        public static string ThemeMode
        {
            get => (LocalSettings.Values["ThemeMode"] as string) ?? "System";
            set => LocalSettings.Values["ThemeMode"] = value;
        }

        public static string SelectedProvider
        {
            get
            {
                string value = (LocalSettings.Values["SelectedProvider"] as string) ?? "ollama";
                return IsCodexProvider(value) ? "codex" : value;
            }
            set => LocalSettings.Values["SelectedProvider"] = value;
        }

        public static string CodexReasoningEffort
        {
            get => (LocalSettings.Values["CodexReasoningEffort"] as string) ?? string.Empty;
            set => LocalSettings.Values["CodexReasoningEffort"] = value;
        }

        public static string CodexChatThreadId
        {
            get => (LocalSettings.Values["CodexChatThreadId"] as string) ?? string.Empty;
            set => LocalSettings.Values["CodexChatThreadId"] = value;
        }

        public static string CodexAssistThreadId
        {
            get => (LocalSettings.Values["CodexAssistThreadId"] as string) ?? string.Empty;
            set => LocalSettings.Values["CodexAssistThreadId"] = value;
        }

        public static string CodexAssistModel
        {
            get => (LocalSettings.Values["CodexAssistModel"] as string) ?? string.Empty;
            set => LocalSettings.Values["CodexAssistModel"] = value;
        }

        /// <summary>
        /// Folder holding the configuration workspace vault (<c>vault.dat</c>) and custom
        /// templates (<c>custom_templates.json</c>). Read once at startup when
        /// the DI singletons are constructed — changes apply after restart.
        /// </summary>
        public static string NetworkConfigDirectory
        {
            get => (LocalSettings.Values["NetworkConfigDirectory"] as string)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Networker");
            set => LocalSettings.Values["NetworkConfigDirectory"] = value;
        }

        /// <summary>
        /// Vendor preselected in the Tools configuration Generate tab. Values are the
        /// GenerateTab vendor display names (see <c>GenerateTab.VendorDisplayNames</c>).
        /// </summary>
        public static string DefaultVendor
        {
            get => (LocalSettings.Values["DefaultVendor"] as string) ?? "Cisco IOS/IOS-XE";
            set => LocalSettings.Values["DefaultVendor"] = value;
        }

        public static string SelectedToolKey
        {
            get => (LocalSettings.Values["SelectedToolKey"] as string) ?? string.Empty;
            set => LocalSettings.Values["SelectedToolKey"] = value;
        }

        public static string GlobalSystemPrompt
        {
            get => GetLocalFileValue("GlobalSystemPrompt.txt");
            set => SetLocalFileValue("GlobalSystemPrompt.txt", value);
        }

        public static string GlobalCustomInstructions
        {
            get => GetLocalFileValue("GlobalCustomInstructions.txt");
            set => SetLocalFileValue("GlobalCustomInstructions.txt", value);
        }

        /// <summary>
        /// Whether the update scheduler runs automatic checks. Defaults on.
        /// </summary>
        public static bool AutomaticUpdateChecksEnabled
        {
            get => LocalSettings.Values["AutomaticUpdateChecksEnabled"] is bool value ? value : true;
            set => LocalSettings.Values["AutomaticUpdateChecksEnabled"] = value;
        }

        /// <summary>
        /// Whether the update channel includes prereleases (opt-in). Defaults off.
        /// </summary>
        public static bool IncludePrereleaseUpdates
        {
            get => LocalSettings.Values["IncludePrereleaseUpdates"] is bool value ? value : false;
            set => LocalSettings.Values["IncludePrereleaseUpdates"] = value;
        }

        /// <summary>
        /// UTC time of the last successful (200 or 304) automatic check, or null.
        /// </summary>
        public static DateTimeOffset? LastSuccessfulUpdateCheckUtc
        {
            get => LocalSettings.Values["LastSuccessfulUpdateCheckUtc"] is DateTimeOffset value ? value : null;
            set => SetOptionalDateTime("LastSuccessfulUpdateCheckUtc", value);
        }

        /// <summary>
        /// The channel the last persisted check ran against ("Stable" or "Preview").
        /// A change makes the next automatic check immediately due.
        /// </summary>
        public static string LastCheckedUpdateChannel
        {
            get => (LocalSettings.Values["LastCheckedUpdateChannel"] as string) ?? "Stable";
            set => LocalSettings.Values["LastCheckedUpdateChannel"] = value;
        }

        /// <summary>
        /// UTC time the next automatic check may run (success cadence, failure
        /// backoff, or rate-limit reset), or null when immediately due.
        /// </summary>
        public static DateTimeOffset? NextAutomaticUpdateCheckUtc
        {
            get => LocalSettings.Values["NextAutomaticUpdateCheckUtc"] is DateTimeOffset value ? value : null;
            set => SetOptionalDateTime("NextAutomaticUpdateCheckUtc", value);
        }

        /// <summary>
        /// Consecutive failed automatic checks, driving the exponential backoff.
        /// </summary>
        public static int UpdateCheckFailureCount
        {
            get => LocalSettings.Values["UpdateCheckFailureCount"] is int value ? value : 0;
            set => LocalSettings.Values["UpdateCheckFailureCount"] = value;
        }

        private static void SetOptionalDateTime(string key, DateTimeOffset? value)
        {
            if (value is null)
            {
                LocalSettings.Values.Remove(key);
            }
            else
            {
                LocalSettings.Values[key] = value.Value;
            }
        }

        // The global prompt is stored as UTF-8 text files (rather than in
        // LocalSettings, which limits each value to 8KB) so that large, multi-line
        // prompts are fully supported and formatting is preserved exactly.
        private static string GetLocalFileValue(string fileName, string fallback = "")
        {
            try
            {
                string path = Path.Combine(GetLocalDataDirectory(), fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                return File.Exists(path) ? File.ReadAllText(path) : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static void SetLocalFileValue(string fileName, string value)
        {
            _ = TrySetLocalFileValue(fileName, value, out _);
        }

        private static bool IsCodexProvider(string? value) => value is not null &&
            (value.Equals("codex", StringComparison.OrdinalIgnoreCase)
             || value.Equals("openai-codex", StringComparison.OrdinalIgnoreCase)
             || value.Equals("chatgpt", StringComparison.OrdinalIgnoreCase)
             || value.Equals("openai-chatgpt", StringComparison.OrdinalIgnoreCase));

        public static bool TrySaveGlobalPrompts(string systemPrompt, string customInstructions, out string error)
        {
            if (!TrySetLocalFileValue("GlobalSystemPrompt.txt", systemPrompt, out error))
            {
                return false;
            }

            return TrySetLocalFileValue("GlobalCustomInstructions.txt", customInstructions, out error);
        }

        private static bool TrySetLocalFileValue(string fileName, string value, out string error)
        {
            string? tempPath = null;
            try
            {
                string path = Path.Combine(GetLocalDataDirectory(), fileName);
                tempPath = path + ".tmp";
                File.WriteAllText(tempPath, value ?? string.Empty);
                File.Move(tempPath, path, overwrite: true);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                if (tempPath is not null)
                {
                    try { File.Delete(tempPath); } catch { }
                }

                error = ex.Message;
                return false;
            }
        }

        internal static string GetLocalDataDirectory()
        {
            try
            {
                return ApplicationData.Current.LocalFolder.Path;
            }
            catch
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Networker");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        internal static string GetTemporaryDataDirectory()
        {
            try
            {
                return ApplicationData.Current.TemporaryFolder.Path;
            }
            catch
            {
                string path = Path.Combine(Path.GetTempPath(), "Networker");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        private sealed class SettingsStore
        {
            private readonly ApplicationDataContainer? _packagedSettings;
            private readonly string _settingsPath;
            private readonly Dictionary<string, object?> _values;

            private SettingsStore(ApplicationDataContainer? packagedSettings, string settingsPath)
            {
                _packagedSettings = packagedSettings;
                _settingsPath = settingsPath;
                _values = Load(settingsPath, packagedSettings);
                Values = new SettingsValues(this);
            }

            public SettingsValues Values { get; }

            public static SettingsStore Create()
            {
                try
                {
                    return new SettingsStore(ApplicationData.Current.LocalSettings, string.Empty);
                }
                catch
                {
                    string directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Networker");
                    return new SettingsStore(null, Path.Combine(directory, "settings.json"));
                }
            }

            private void Save()
            {
                if (_packagedSettings is not null)
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                string tempPath = _settingsPath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(_values));
                File.Move(tempPath, _settingsPath, overwrite: true);
            }

            private static Dictionary<string, object?> Load(
                string settingsPath,
                ApplicationDataContainer? packagedSettings)
            {
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (packagedSettings is not null || !File.Exists(settingsPath))
                {
                    return values;
                }

                try
                {
                    var jsonValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        File.ReadAllText(settingsPath));
                    if (jsonValues is null)
                    {
                        return values;
                    }

                    foreach ((string key, JsonElement value) in jsonValues)
                    {
                        values[key] = value.ValueKind switch
                        {
                            JsonValueKind.String when value.TryGetDateTimeOffset(out DateTimeOffset date) => date,
                            JsonValueKind.String => value.GetString(),
                            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => null,
                        };
                    }
                }
                catch (JsonException)
                {
                    // Ignore corrupt unpackaged settings and start with defaults.
                }

                return values;
            }

            public sealed class SettingsValues
            {
                private readonly SettingsStore _owner;

                public SettingsValues(SettingsStore owner)
                {
                    _owner = owner;
                }

                public object? this[string key]
                {
                    get => _owner._packagedSettings is not null
                        ? _owner._packagedSettings.Values[key]
                        : _owner._values.GetValueOrDefault(key);
                    set
                    {
                        if (_owner._packagedSettings is not null)
                        {
                            _owner._packagedSettings.Values[key] = value;
                            return;
                        }

                        _owner._values[key] = value;
                        _owner.Save();
                    }
                }

                public bool Remove(string key)
                {
                    if (_owner._packagedSettings is not null)
                    {
                        return _owner._packagedSettings.Values.Remove(key);
                    }

                    bool removed = _owner._values.Remove(key);
                    if (removed)
                    {
                        _owner.Save();
                    }
                    return removed;
                }
            }
        }
    }
}
