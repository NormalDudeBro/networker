using System;
using System.IO;
using Windows.Storage;

namespace networker
{
    public static class AppSettings
    {
        private static ApplicationDataContainer LocalSettings = ApplicationData.Current.LocalSettings;

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
            get => (LocalSettings.Values["SelectedModel"] as string) ?? "";
            set => LocalSettings.Values["SelectedModel"] = value;
        }

        public static string ThemeMode
        {
            get => (LocalSettings.Values["ThemeMode"] as string) ?? "System";
            set => LocalSettings.Values["ThemeMode"] = value;
        }

        public static string SelectedProvider
        {
            get => (LocalSettings.Values["SelectedProvider"] as string) ?? "ollama";
            set => LocalSettings.Values["SelectedProvider"] = value;
        }

        /// <summary>
        /// Folder holding the Network Config vault (<c>vault.dat</c>) and custom
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
        /// Vendor preselected in the Network Config Generate tab. Values are the
        /// GenerateTab vendor display names (see <c>GenerateTab.VendorDisplayNames</c>).
        /// </summary>
        public static string DefaultVendor
        {
            get => (LocalSettings.Values["DefaultVendor"] as string) ?? "Cisco IOS/IOS-XE";
            set => LocalSettings.Values["DefaultVendor"] = value;
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

        // The global prompt is stored as UTF-8 text files (rather than in
        // LocalSettings, which limits each value to 8KB) so that large, multi-line
        // prompts are fully supported and formatting is preserved exactly.
        private static string GetLocalFileValue(string fileName, string fallback = "")
        {
            try
            {
                string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, fileName);
                return File.Exists(path) ? File.ReadAllText(path) : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static void SetLocalFileValue(string fileName, string value)
        {
            try
            {
                string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, fileName);
                File.WriteAllText(path, value ?? "");
            }
            catch
            {
                // Persisting the global prompt is best-effort; a storage failure
                // must never crash the application.
            }
        }
    }
}