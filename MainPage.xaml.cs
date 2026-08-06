using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using networker.Controls;
using networker.Models;
using networker.Services;
using NetOps.Core.NetTools.Config;
using NetOps.Core.NetTools.Ip;
using NetOps.Core.NetTools.Logs;
using NetOps.Core.NetTools.Playbooks;
using NetOps.Core.NetTools.Topology;

namespace networker
{
    public sealed partial class MainPage : Page
    {
        public static MainPage? Current { get; private set; }

        private readonly ObservableCollection<ChatMessage> _messages = new();

        public MainPage()
        {
            this.InitializeComponent();
            MessagesList.ItemsSource = _messages;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Current = this;

            if (ProviderComboBox.Items.Count == 0)
            {
                ProviderComboBox.Items.Add("ollama");
                ProviderComboBox.Items.Add("grok");
                ProviderComboBox.Items.Add("gemini");
                ProviderComboBox.SelectedItem = AppSettings.SelectedProvider;
                if (ProviderComboBox.SelectedItem is null)
                {
                    ProviderComboBox.SelectedIndex = 0;
                }
            }

            UpdateProviderLabel();
            _ = RefreshConnectionAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (Current == this) Current = null;
        }

        // ============================ Sending ============================

        private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendAsync();

        private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter &&
                (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                 .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)))
            {
                e.Handled = true;
                _ = SendAsync();
            }
        }

        private async Task SendAsync()
        {
            string text = InputBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            if (string.IsNullOrWhiteSpace(AppSettings.SelectedModel))
            {
                Toaster.Show("No model selected. Refresh models in the Assistant panel or Settings.", InfoBarSeverity.Warning, "Model required");
                return;
            }

            var userMessage = new ChatMessage { Role = ChatRole.User, Text = text };
            _messages.Add(userMessage);
            InputBox.Text = "";
            ShowChat();

            var assistant = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = "",
                IsStreaming = true,
                Provider = AppSettings.SelectedProvider,
                Model = AppSettings.SelectedModel
            };
            _messages.Add(assistant);
            SetBusy(true);

            try
            {
                await foreach (var token in ChatService.StreamAsync(text))
                {
                    assistant.Text += token;
                }
            }
            catch (Exception ex)
            {
                _messages.Add(new ChatMessage { Role = ChatRole.Error, Text = ex.Message });
                Toaster.Show(ex.Message, InfoBarSeverity.Error, "Request failed");
            }
            finally
            {
                assistant.IsStreaming = false;
                SetBusy(false);
                ScrollToBottom();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            LlmRuntime.Router.Cancel();
            Toaster.Show("Request cancelled.", InfoBarSeverity.Informational, "Cancelled");
        }

        private void SetBusy(bool busy)
        {
            SendButton.IsEnabled = !busy;
            CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowChat()
        {
            EmptyState.Visibility = Visibility.Collapsed;
            MessagesList.Visibility = Visibility.Visible;
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            if (_messages.Count > 0)
            {
                MessagesList.ScrollIntoView(_messages[^1]);
            }
        }

        // ============================ Quick actions ============================

        private readonly Dictionary<string, string> _quickTemplates = new()
        {
            ["ip"] = "Calculate the subnet details for 192.168.10.0/24, including usable host range and wildcard mask.",
            ["ospf"] = "Generate an OSPF configuration for a Cisco IOS router in area 0 on networks 10.0.0.0/24 and 10.0.1.0/24.",
            ["bgp"] = "Generate a BGP configuration for a Cisco IOS router announcing 203.0.113.0/24 to neighbor 192.0.2.1.",
            ["audit"] = "Audit the following network device configuration and report security and best-practice issues:\n\n",
            ["bgp-trouble"] = "Troubleshoot a BGP peer that keeps flapping between the two routers in my network.",
            ["explain"] = "Explain why BGP sessions flap and what checks to run first.",
            ["logs"] = "Analyze the following device logs and tell me what needs attention:\n\n"
        };

        private void QuickStart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag }) return;
            if (_quickTemplates.TryGetValue(tag, out string? template) && template is not null)
            {
                InputBox.Text = template;
                InputBox.Focus(FocusState.Programmatic);
            }
        }

        // ============================ Tool execution ============================

        private async void RunTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag }) return;
            await RunToolAsync(tag, InputBox.Text);
        }

        private async Task RunToolAsync(string tool, string input)
        {
            ChatMessage? resultMessage = null;

            try
            {
                resultMessage = tool switch
                {
                    "ip" => await RunIpCalculator(input),
                    "ospf" => await RunConfigGenerator(ConfigPlatform.CiscoIosXe, input, "OSPF"),
                    "bgp" => await RunConfigGenerator(ConfigPlatform.CiscoIosXe, input, "BGP"),
                    "audit" => await RunConfigAudit(input),
                    "logs" => await RunLogAnalyzer(input),
                    "topology" => await RunTopology(input),
                    "translate" => await RunTranslator(input),
                    "playbook" => await RunPlaybook(input),
                    _ => null,
                };
            }
            catch (Exception ex)
            {
                Toaster.Show(ex.Message, InfoBarSeverity.Error, "Tool error");
                return;
            }

            if (resultMessage is not null)
            {
                _messages.Add(resultMessage);
                ShowChat();
                ScrollToBottom();
                AddToolActivity(resultMessage);
            }
        }

        private static async Task<ChatMessage?> RunIpCalculator(string cidr)
        {
            var text = cidr.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return new ChatMessage { Role = ChatRole.Assistant, IsCode = true, CodeTitle = "Subnet Calculator", Text = "Enter a CIDR (e.g. 192.168.10.0/24)." };
            }

            try
            {
                var s = NetOps.Core.NetTools.Ip.IpToolkit.Calculate(text);
                var sb = new StringBuilder();
                sb.AppendLine($"Input          {s.Input}");
                sb.AppendLine($"Network        {s.NetworkAddress}/{s.PrefixLength}");
                sb.AppendLine($"Netmask        {s.Netmask}");
                sb.AppendLine($"Wildcard       {s.WildcardMask}");
                sb.AppendLine($"First usable   {s.FirstUsable}");
                sb.AppendLine($"Last usable    {s.LastUsable}");
                if (s.BroadcastAddress is not null)
                {
                    sb.AppendLine($"Broadcast      {s.BroadcastAddress}");
                }
                sb.AppendLine($"Total hosts    {s.TotalHosts}");
                sb.AppendLine($"Usable hosts   {s.UsableHosts}");
                sb.AppendLine($"Private        {(s.IsPrivate ? "yes" : "no")}");
                if (!string.IsNullOrWhiteSpace(s.Description))
                {
                    sb.AppendLine($"Notes          {s.Description}");
                }

                return new ChatMessage { Role = ChatRole.Assistant, IsCode = true, CodeTitle = "Subnet Calculator", Text = sb.ToString().TrimEnd() };
            }
            catch (Exception ex)
            {
                return new ChatMessage { Role = ChatRole.Error, Text = ex.Message };
            }
        }

        private static async Task<ChatMessage?> RunConfigGenerator(ConfigPlatform platform, string prompt, string feature)
        {
            // Use a sensible default spec; in future we can parse the prompt for details
            var spec = new NetOps.Core.NetTools.Config.DeviceSpec
            {
                Hostname = "edge-01",
                DomainName = "corp.example",
                EnableSecret = "$9$hashed",
                Username = "admin",
                UsernameSecret = "$9$adminhash",
                SnmpCommunity = "n0t-public",
                LoggingHost = "10.99.0.5",
                NtpServer = "192.0.2.123",
                RouterId = "10.0.0.1",
                BgpAsn = "65001",
                BgpNetworks = new[] { "10.10.0.0/16" },
                BgpRedistributeConnected = true,
                Vlans = new[]
                {
                    new NetOps.Core.NetTools.Config.VlanSpec { Id = "10", Name = "users", InterfaceVlanIp = "192.168.10.1/24" },
                    new NetOps.Core.NetTools.Config.VlanSpec { Id = "20", Name = "servers", InterfaceVlanIp = "192.168.20.1/24" },
                },
                Interfaces = new[]
                {
                    new NetOps.Core.NetTools.Config.InterfaceSpec { Name = "GigabitEthernet0/0", Description = "Uplink", Mode = "routed", Ip = "203.0.113.1/30", Mtu = "1500" },
                    new NetOps.Core.NetTools.Config.InterfaceSpec { Name = "GigabitEthernet0/1", Mode = "access", Vlan = "10" },
                    new NetOps.Core.NetTools.Config.InterfaceSpec { Name = "GigabitEthernet0/2", Mode = "trunk", AllowedVlans = "10,20" },
                },
                OspfAreas = new[]
                {
                    new NetOps.Core.NetTools.Config.OspfAreaSpec("192.168.10.0/24", "0"),
                    new NetOps.Core.NetTools.Config.OspfAreaSpec("192.168.20.0/24", "0"),
                },
                BgpNeighbors = new[]
                {
                    new NetOps.Core.NetTools.Config.BgpNeighborSpec("203.0.113.2", "64512", "Transit"),
                },
                Acls = new[]
                {
                    new NetOps.Core.NetTools.Config.AclEntrySpec { Name = "MGMT-IN", Action = "permit", Protocol = "tcp", Source = "10.0.0.0/8", Destination = "any", DestinationPort = "22" },
                    new NetOps.Core.NetTools.Config.AclEntrySpec { Name = "MGMT-IN", Action = "deny", Protocol = "tcp", Source = "any", Destination = "any", DestinationPort = "23", Log = true },
                },
                Nat = new NetOps.Core.NetTools.Config.NatSpec
                {
                    Inside = new[] { "GigabitEthernet0/1" },
                    Outside = "GigabitEthernet0/0",
                    AclName = "NAT-ACL",
                },
            };

            var config = NetOps.Core.NetTools.Config.ConfigGenerator.Generate(platform, spec);
            return new ChatMessage { Role = ChatRole.Assistant, IsCode = true, CodeTitle = $"{platform} {feature} Config", Text = config };
        }

        private static async Task<ChatMessage?> RunConfigAudit(string config)
        {
            var findings = NetOps.Core.NetTools.Config.ConfigAuditor.Audit(config);
            if (findings.Count == 0)
            {
                return new ChatMessage { Role = ChatRole.Assistant, IsCode = true, CodeTitle = "Config Audit", Text = "No issues found." };
            }

            var sb = new StringBuilder();
            sb.AppendLine("| Line | Severity | Rule | Description |");
            sb.AppendLine("|------|----------|------|-------------|");
            foreach (var f in findings)
            {
                sb.AppendLine($"| {f.LineNumber} | {f.Severity} | {f.RuleId} | {f.Title} |");
            }

            return new ChatMessage { Role = ChatRole.Assistant, IsCode = true, CodeTitle = "Config Audit", Text = sb.ToString() };
        }

        private static async Task<ChatMessage?> RunLogAnalyzer(string logs)
        {
            var lines = (logs ?? "").Split('\n');
            var analysis = NetOps.Core.NetTools.Logs.LogAnalyzer.Analyze(lines);
            if (analysis.Findings.Count == 0)
            {
                return new ChatMessage { Role = ChatRole.Assistant, IsCode = true, CodeTitle = "Log Analysis", Text = "No anomalies detected." };
            }

            var sb = new StringBuilder();
            sb.AppendLine("| Line | Severity | Rule | Description |");
            sb.AppendLine("|------|----------|------|-------------|");
            foreach (var f in analysis.Findings)
            {
                sb.AppendLine($"| {f.LineNumber} | {f.Severity} | {f.RuleId} | {f.Description} |");
            }

            return new ChatMessage { Role = ChatRole.Assistant, IsCode = true, CodeTitle = "Log Analysis", Text = sb.ToString() };
        }

        private static async Task<ChatMessage?> RunTopology(string text)
        {
            var configs = ParseDeviceConfigs(text);
            if (configs.Count == 0) return null;

            var topology = NetOps.Core.NetTools.Topology.TopologyBuilder.Build(configs);
            var mermaid = NetOps.Core.NetTools.Topology.TopologyBuilder.RenderMermaid(topology);
            return new ChatMessage { Role = ChatRole.Assistant, IsCode = true, CodeTitle = "Topology (Mermaid)", Text = mermaid };
        }

        private static async Task<ChatMessage?> RunTranslator(string input)
        {
            var output = NetOps.Core.NetTools.Config.ConfigTranslator.IosToJunos(input);
            return new ChatMessage { Role = ChatRole.Assistant, IsCode = true, CodeTitle = "Juniper Junos (translated)", Text = output };
        }

        private static async Task<ChatMessage?> RunPlaybook(string scenario)
        {
            var key = scenario switch
            {
                "bgp-flap" => "bgp-flap",
                "high-cpu" => "high-cpu",
                "interface-down" => "interface-down",
                "ospf-adjacency" => "ospf-adjacency",
                "security-hardening" => "security-hardening",
                _ => "new-switch",
            };

            var playbook = NetOps.Core.NetTools.Playbooks.PlaybookGenerator.Generate(key);
            var md = NetOps.Core.NetTools.Playbooks.PlaybookGenerator.RenderMarkdown(playbook);
            return new ChatMessage { Role = ChatRole.Assistant, IsCode = true, CodeTitle = $"{scenario} Playbook", Text = md };
        }

        private static List<NetOps.Core.NetTools.Topology.DeviceConfig> ParseDeviceConfigs(string text)
        {
            var result = new List<NetOps.Core.NetTools.Topology.DeviceConfig>();
            var blocks = text.Split(new[] { "\r\n==== ", "==== ", "===" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                var name = block.Split('\n', 2)[0].Trim();
                var body = block.Contains('\n') ? block.Split('\n', 2)[1] : string.Empty;
                if (body.Length > 0)
                {
                    result.Add(new NetOps.Core.NetTools.Topology.DeviceConfig(name.Trim(), body));
                }
            }
            return result;
        }

        private void AddToolActivity(ChatMessage message)
        {
            ToolActivityPlaceholder.Visibility = Visibility.Collapsed;
            var card = new Border
            {
                Style = (Style)Application.Current.Resources["CardStyle"],
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = message.CodeTitle ?? "Tool Result", Style = (Style)Application.Current.Resources["CardHeaderStyle"] },
                        new networker.Controls.CodeBlockView { DataContext = message }
                    }
                }
            };
            ToolActivityList.Children.Insert(0, card);
        }

        // ============================ Header / panel ============================

        private void PaletteButton_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.OpenPalette();

        private void ThemeButton_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.ToggleTheme();

        private void PanelToggleButton_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = AssistantPanel.Visibility == Visibility.Visible;
            AssistantPanel.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void NewChatButton_Click(object sender, RoutedEventArgs e) => NewChat();

        public void NewChat()
        {
            _messages.Clear();
            MessagesList.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            InputBox.Text = "";
            RefreshHistory();
        }

        public void ClearHistory()
        {
            if (_messages.Count == 0)
            {
                Toaster.Show("No messages to clear.", InfoBarSeverity.Informational);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Clear history?",
                Content = "This removes all conversation messages from the current workspace. This cannot be undone.",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            dialog.PrimaryButtonClick += (s, e) =>
            {
                _messages.Clear();
                RefreshHistory();
                NewChat();
                Toaster.Show("History cleared.", InfoBarSeverity.Success);
            };
            _ = dialog.ShowAsync();
        }

        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e) => ClearHistory();

        // ============================ Provider / models / health ============================

        private void UpdateProviderLabel()
        {
            ProviderText.Text = AppSettings.SelectedProvider;
            ModelText.Text = string.IsNullOrWhiteSpace(AppSettings.SelectedModel) ? "no model" : AppSettings.SelectedModel;
        }

        private async void HealthCheckButton_Click(object sender, RoutedEventArgs e) => await RefreshConnectionAsync();

        public async Task RefreshConnectionAsync()
        {
            PanelHealthText.Text = "Checking…";
            PanelHealthDot.Fill = new SolidColorBrush(Colors.Gray);
            HealthText.Text = "Checking";
            HealthDot.Fill = new SolidColorBrush(Colors.Gray);

            bool connected = false;
            try
            {
                var status = await LlmRuntime.GetSelectedProviderHealthAsync(AppSettings.SelectedProvider);
                connected = status.IsAvailable;
                if (!connected)
                {
                    SetUnhealthy(status.Message ?? "Provider unavailable");
                }
            }
            catch (Exception ex)
            {
                SetUnhealthy(ex.Message);
            }

            bool hasModels = await LoadModelsAsync();

            if (connected)
            {
                SetHealthy(hasModels);
            }

            UpdateProviderLabel();
        }

        public void RefreshConnection()
        {
            _ = RefreshConnectionAsync();
        }

        private void SetHealthy(bool hasModels)
        {
            var green = new SolidColorBrush(Colors.LightGreen);
            PanelHealthDot.Fill = green;
            HealthDot.Fill = green;
            PanelHealthText.Text = "Connected";
            HealthText.Text = "Connected";

            if (!hasModels)
            {
                PanelHealthText.Text = "Connected — no models";
            }
        }

        private void SetUnhealthy(string message)
        {
            var red = new SolidColorBrush(Colors.OrangeRed);
            PanelHealthDot.Fill = red;
            HealthDot.Fill = red;
            string shortMessage = message.Length > 60 ? message[..60] : message;
            PanelHealthText.Text = $"Offline: {shortMessage}";
            HealthText.Text = "Offline";
        }

        private async Task<bool> LoadModelsAsync()
        {
            ModelLoadingRing.IsActive = true;
            try
            {
                var models = await LlmRuntime.GetModelsAsync();
                if (models.Count == 0)
                {
                    ModelComboBox.ItemsSource = null;
                    ModelComboBox.IsEnabled = false;
                    AppSettings.SelectedModel = "";
                    return false;
                }

                var ids = models.Select(m => m.Id).ToList();
                ModelComboBox.IsEnabled = true;
                ModelComboBox.ItemsSource = ids;

                string previous = AppSettings.SelectedModel;
                ModelComboBox.SelectedItem = !string.IsNullOrEmpty(previous) && ids.Contains(previous)
                    ? previous
                    : ids[0];
                if (ModelComboBox.SelectedItem is string selected)
                {
                    AppSettings.SelectedModel = selected;
                }

                return true;
            }
            catch
            {
                ModelComboBox.ItemsSource = null;
                ModelComboBox.IsEnabled = false;
                return false;
            }
            finally
            {
                ModelLoadingRing.IsActive = false;
                UpdateProviderLabel();
            }
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProviderComboBox.SelectedItem is string provider)
            {
                AppSettings.SelectedProvider = provider;
                LlmRuntime.ApplyProviderSelection(provider, AppSettings.SelectedModel);
                UpdateProviderLabel();
                _ = RefreshConnectionAsync();
            }
        }

        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModelComboBox.SelectedItem is string model)
            {
                AppSettings.SelectedModel = model;
                LlmRuntime.ApplyProviderSelection(AppSettings.SelectedProvider, model);
                UpdateProviderLabel();
            }
        }

        // ============================ History ============================

        private void RefreshHistory()
        {
            HistoryList.ItemsSource = null;
            HistoryList.ItemsSource = FilterHistory();
        }

        private IReadOnlyList<ChatMessage> FilterHistory()
        {
            string query = (HistorySearchBox.Text ?? "").Trim();
            var all = _messages.Reverse().ToList();
            if (string.IsNullOrEmpty(query))
            {
                return all.Take(100).ToList();
            }
            return all.Where(m => m.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(100).ToList();
        }

        private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshHistory();

        private void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ChatMessage message)
            {
                MessagesList.ScrollIntoView(message);
            }
        }

        // ============================ Input growth ============================

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            double lines = InputBox.Text.Split('\n').Length;
            double height = Math.Clamp(24 + (lines * 20), 36, 160);
            InputBox.Height = height;
        }
    }
}
