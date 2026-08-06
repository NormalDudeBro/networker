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
    }
}