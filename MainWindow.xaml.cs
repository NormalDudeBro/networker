using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using networker.Controls;
using networker.Services;
using networker.Views;
using Networker.Core.Workflow;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace networker
{
    public sealed partial class MainWindow : Window
    {
        private const int MinWindowWidth = 720;
        private const int MinWindowHeight = 540;
        private bool _enforcingMinimumSize;
        private bool _selectingTab;
        private TroubleshootingSession? _session;
        private ChatGptWebSession? _chatGptSession;

        public static MainWindow? Instance { get; private set; }
        public ObservableCollection<WorkflowTabItem> WorkflowItems { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;

            Root.Loaded += (_, _) =>
            {
                EnforceMinimumWindowSize();
                try { ((App)Application.Current).Services.GetRequiredService<LaunchHealthService>().SignalHealthy(); } catch { }
            };
            AppWindow.Changed += (_, args) => { if (args.DidSizeChange) EnforceMinimumWindowSize(); };
            Closed += MainWindow_Closed;

            ApplyThemeToRoot();
            Toaster.Initialize(ToastHost, DispatcherQueue);
            try
            {
                _chatGptSession = ((App)Application.Current).Services.GetRequiredService<ChatGptWebSession>();
                _chatGptSession.Attach(ChatGptWebView, ShowChatGptBrowser, HideChatGptBrowser);
                LlmRuntime.ConfigureChatGptTransport(_chatGptSession);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ChatGPT browser initialization unavailable: {ex.Message}");
            }
            LlmSession.Initialize();
            LlmSession.Changed += LlmSession_Changed;

            try
            {
                _session = ((App)Application.Current).Services.GetRequiredService<TroubleshootingSession>();
                _session.Changed += Session_Changed;
            }
            catch (Exception)
            {
                // Core app navigation remains available if an optional service failed.
            }

            BuildWorkflowItems();
            BuildPaletteCommands();
            UpdateStatusBar();

            WorkflowStage initial = _session?.Current.SelectedStage ?? WorkflowStage.Start;
            NavigateToStage(initial);
            _ = LlmSession.RefreshAsync();
        }

        public void OpenPalette() => Palette.Open();

        public void ToggleTheme()
        {
            AppSettings.ThemeMode = AppSettings.ThemeMode switch
            {
                "System" => "Light",
                "Light" => "Dark",
                _ => "System"
            };
            ApplyThemeToRoot();
        }

        public void ApplyThemeToFramePublic() => ApplyThemeToRoot();

        public void NavigateTo(string tag)
        {
            if (WorkflowStageCatalog.TryFind(tag, out var stage))
            {
                NavigateToStage(stage.Stage);
                return;
            }

            NavigateToStage(tag switch
            {
                "home" => WorkflowStage.Start,
                "assistant" => WorkflowStage.Assist,
                "tools" => WorkflowStage.Inspect,
                "settings" => WorkflowStage.Settings,
                _ => WorkflowStage.Start,
            });
        }

        public void NavigateToTool(string key)
        {
            if (!WorkflowStageCatalog.TryFindLegacyTool(key, out var route)) return;
            NavigateToStage(route.Stage, key);
        }

        public void NavigateToStage(WorkflowStage stage, string? toolKey = null)
        {
            if (stage == WorkflowStage.Assist && !ChatService.IsModelSelected)
            {
                WorkflowAnnouncement.Text = "Assist needs an AI model. Configure one in Settings.";
                stage = WorkflowStage.Settings;
            }

            Type pageType = stage switch
            {
                WorkflowStage.Start => typeof(DashboardPage),
                >= WorkflowStage.Inspect and <= WorkflowStage.Resolve => typeof(WorkflowPage),
                WorkflowStage.Assist => typeof(AssistantPage),
                _ => typeof(SettingsPage),
            };

            if (ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType, toolKey ?? WorkflowStageCatalog.Get(stage).Key);
            }
            else if (ContentFrame.Content is WorkflowPage workflow)
            {
                workflow.SelectStage(stage, toolKey);
            }
            else if (ContentFrame.Content is SettingsPage settings && toolKey is not null)
            {
                settings.SelectSection(toolKey);
            }

            _session?.SelectStage(stage);
            SelectWorkflowItem(stage);
            UpdateNavigationActions(stage);
        }

        public void OpenAssistantNewChat()
        {
            NavigateToStage(WorkflowStage.Assist);
            AssistantPage.Current?.NewChat();
        }

        private void BuildWorkflowItems()
        {
            WorkflowItems.Clear();
            foreach (var definition in WorkflowStageCatalog.All)
            {
                WorkflowItems.Add(new WorkflowTabItem(definition));
            }
            RefreshWorkflowStates();
        }

        private void RefreshWorkflowStates()
        {
            foreach (var item in WorkflowItems)
            {
                var progress = _session?.Current.GetProgress(item.Stage);
                bool disabled = item.Stage == WorkflowStage.Assist && !ChatService.IsModelSelected;
                item.Update(progress?.State ?? WorkflowProgressState.Available, progress?.Message, disabled);
            }
        }

        private void SelectWorkflowItem(WorkflowStage stage)
        {
            _selectingTab = true;
            try
            {
                var item = WorkflowItems.First(i => i.Stage == stage);
                WorkflowTabs.SelectedItem = item;
                WorkflowTabs.ScrollIntoView(item);
            }
            finally
            {
                _selectingTab = false;
            }
        }

        private void UpdateNavigationActions(WorkflowStage stage)
        {
            var definition = WorkflowStageCatalog.Get(stage);
            StageDescription.Text = definition.Description;
            PreviousButton.IsEnabled = stage != WorkflowStage.Start;
            NextButton.IsEnabled = stage != WorkflowStage.Settings;
            PreviousButton.SetValue(AutomationProperties.NameProperty, stage == WorkflowStage.Start
                ? "No previous stage"
                : $"Go to previous stage, {WorkflowStageCatalog.Get(stage - 1).Number} {WorkflowStageCatalog.Get(stage - 1).Label}");
            NextButton.SetValue(AutomationProperties.NameProperty, stage == WorkflowStage.Settings
                ? "No next stage"
                : $"Go to next stage, {WorkflowStageCatalog.Get(stage + 1).Number} {WorkflowStageCatalog.Get(stage + 1).Label}");
        }

        private void BuildPaletteCommands()
        {
            var commands = WorkflowStageCatalog.All.Select(d => new PaletteCommand(
                $"Go to {d.Number} {d.Label}", d.Description, "\uE8A5", "Workflow", d.Number.ToString(),
                () => NavigateToStage(d.Stage), d.Key, d.Label)).ToList();

            commands.AddRange(Models.ToolDescriptor.All.Select(tool => new PaletteCommand(
                $"Open {tool.Header}", tool.Description, tool.Glyph, tool.DisplayPath, string.Empty,
                () => NavigateToTool(tool.Key), tool.Aliases.Prepend(tool.Key).ToArray())));
            commands.Add(new PaletteCommand("New chat", "Start a fresh Assist conversation", "\uE8BD", "Actions", string.Empty, OpenAssistantNewChat, "chat"));
            commands.Add(new PaletteCommand("Cycle theme", "Switch System, Light, and Dark", "\uE790", "Actions", string.Empty, ToggleTheme, "theme"));
            Palette.SetCommands(commands);
        }

        private void WorkflowTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectingTab || WorkflowTabs.SelectedItem is not WorkflowTabItem item) return;
            if (item.IsDisabled)
            {
                WorkflowAnnouncement.Text = item.HelpText;
                SelectWorkflowItem(_session?.Current.SelectedStage ?? WorkflowStage.Start);
                return;
            }
            NavigateToStage(item.Stage);
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            var stage = _session?.Current.SelectedStage ?? WorkflowStage.Start;
            if (stage > WorkflowStage.Start) NavigateToStage(stage - 1);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            var stage = _session?.Current.SelectedStage ?? WorkflowStage.Start;
            if (stage < WorkflowStage.Settings) NavigateToStage(stage + 1);
        }

        private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            int number = e.Key switch
            {
                >= VirtualKey.Number1 and <= VirtualKey.Number9 => (int)e.Key - (int)VirtualKey.Number0,
                >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => (int)e.Key - (int)VirtualKey.NumberPad0,
                _ => 0,
            };
            if (number == 0) return;

            bool modifier = IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu) || IsDown(VirtualKey.Shift)
                || IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows);
            bool textEntry = IsTextEntry(FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject);
            if (!WorkflowNavigationPolicy.TryGetStageForNumber(number, textEntry, modifier, out var stage)) return;

            e.Handled = true;
            NavigateToStage(stage);
        }

        private static bool IsDown(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

        private static bool IsTextEntry(DependencyObject? element)
        {
            while (element is not null)
            {
                if (element is TextBox or PasswordBox or RichEditBox or AutoSuggestBox) return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private void Session_Changed()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RefreshWorkflowStates();
                var stage = _session?.Current.SelectedStage ?? WorkflowStage.Start;
                SelectWorkflowItem(stage);
                UpdateNavigationActions(stage);
            });
        }

        private void LlmSession_Changed() => DispatcherQueue.TryEnqueue(() =>
        {
            UpdateStatusBar();
            RefreshWorkflowStates();
        });

        private void UpdateStatusBar()
        {
            StatusText.Text = string.IsNullOrWhiteSpace(LlmSession.Model)
                ? LlmSession.StatusMessage
                : $"{LlmSession.Provider} / {LlmSession.Model}";
            StatusDot.Fill = (SolidColorBrush)Application.Current.Resources[
                LlmSession.IsChecking ? "AppTextDisabledBrush" : LlmSession.IsConnected ? "AppOnlineBrush" : "AppOfflineBrush"];
        }

        private void StatusRefreshButton_Click(object sender, RoutedEventArgs e) => _ = LlmSession.RefreshAsync();
        private void ThemeButton_Click(object sender, RoutedEventArgs e) => ToggleTheme();
        private async void CloseChatGptBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (_chatGptSession is not null) await _chatGptSession.HideLoginAsync();
            else HideChatGptBrowser();
        }

        public void ShowChatGptBrowser()
        {
            ChatGptBrowserLayer.IsHitTestVisible = true;
            ChatGptBrowserLayer.Background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
            ChatGptBrowserPanel.Width = double.NaN;
            ChatGptBrowserPanel.Height = double.NaN;
            ChatGptBrowserPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            ChatGptBrowserPanel.VerticalAlignment = VerticalAlignment.Stretch;
            ChatGptBrowserPanel.Opacity = 1;
        }

        public void HideChatGptBrowser()
        {
            ChatGptBrowserLayer.IsHitTestVisible = false;
            ChatGptBrowserLayer.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ChatGptBrowserPanel.Width = 1;
            ChatGptBrowserPanel.Height = 1;
            ChatGptBrowserPanel.HorizontalAlignment = HorizontalAlignment.Left;
            ChatGptBrowserPanel.VerticalAlignment = VerticalAlignment.Bottom;
            ChatGptBrowserPanel.Opacity = 0;
        }
        private void PaletteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; Palette.Open(); }
        private void RefreshHealthAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; _ = LlmSession.RefreshAsync(); }

        private void ApplyThemeToRoot()
        {
            Root.RequestedTheme = AppSettings.ThemeMode switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        private void EnforceMinimumWindowSize()
        {
            if (_enforcingMinimumSize) return;
            var size = AppWindow.Size;
            var scale = Root.XamlRoot?.RasterizationScale ?? 1d;
            int width = Math.Max(size.Width, (int)Math.Ceiling(MinWindowWidth * scale));
            int height = Math.Max(size.Height, (int)Math.Ceiling(MinWindowHeight * scale));
            if (width == size.Width && height == size.Height) return;
            _enforcingMinimumSize = true;
            AppWindow.Resize(new SizeInt32(width, height));
            _enforcingMinimumSize = false;
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _session?.SaveNow();
            if (_session is not null) _session.Changed -= Session_Changed;
            LlmSession.Changed -= LlmSession_Changed;
            try { ((App)Application.Current).Services.GetRequiredService<AgentService>().Stop(); } catch { }
            _chatGptSession?.Dispose();
            try { ((App)Application.Current).Services.GetRequiredService<LaunchHealthService>().Dispose(); } catch { }
        }
    }

    public sealed class WorkflowTabItem : INotifyPropertyChanged
    {
        private string _statusText = "Available";
        private string _statusGlyph = "\uE73E";
        private Brush? _statusBrush;
        private Brush? _numberBackground;
        private string _helpText = string.Empty;
        private bool _isDisabled;

        public WorkflowTabItem(WorkflowStageDefinition definition)
        {
            Stage = definition.Stage;
            Number = definition.Number;
            Label = definition.Label;
            Description = definition.Description;
            Update(WorkflowProgressState.Available, null, false);
        }

        public WorkflowStage Stage { get; }
        public int Number { get; }
        public string Label { get; }
        public string Description { get; }
        public string AutomationName => $"Step {Number} of 9, {Label}, {StatusText}";
        public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
        public string StatusGlyph { get => _statusGlyph; private set => Set(ref _statusGlyph, value); }
        public Brush? StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
        public Brush? NumberBackground { get => _numberBackground; private set => Set(ref _numberBackground, value); }
        public string HelpText { get => _helpText; private set => Set(ref _helpText, value); }
        public bool IsDisabled { get => _isDisabled; private set => Set(ref _isDisabled, value); }

        public void Update(WorkflowProgressState state, string? message, bool disabled)
        {
            IsDisabled = disabled;
            string brushKey;
            if (disabled)
            {
                StatusText = "Disabled";
                StatusGlyph = "\uE72E";
                brushKey = "AppTextDisabledBrush";
                HelpText = "Select an AI model in Settings to enable Assist.";
            }
            else if (state == WorkflowProgressState.Error)
            {
                StatusText = "Needs attention";
                StatusGlyph = "\uE783";
                brushKey = "AppDangerBrush";
                HelpText = message ?? "This stage has an error.";
            }
            else if (state == WorkflowProgressState.Completed)
            {
                StatusText = "Completed";
                StatusGlyph = "\uE73E";
                brushKey = "AppSuccessBrush";
                HelpText = message ?? "Stage completed.";
            }
            else
            {
                StatusText = "Available";
                StatusGlyph = "\uE73C";
                brushKey = "AppTextSecondaryBrush";
                HelpText = Description;
            }

            StatusBrush = (Brush)Application.Current.Resources[brushKey];
            NumberBackground = (Brush)Application.Current.Resources[state == WorkflowProgressState.Completed ? "BadgeSuccessBackgroundBrush" : "AppInsetBrush"];
            OnPropertyChanged(nameof(AutomationName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnPropertyChanged(name);
        }
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
