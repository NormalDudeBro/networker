using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Networker.Core.NetTools.Config;
using Networker.Core.NetTools.Ip;
using Networker.Core.NetTools.Logs;
using Networker.Core.NetTools.Playbooks;
using Networker.Core.NetTools.Topology;
using Networker.Core.Prompting;
using networker.Controls;
using networker.Models;
using networker.Services;

namespace networker
{
    public sealed partial class ToolsPage : Page
    {
        private static readonly string SampleDeviceSpec = """
            {
              "hostname": "edge-01",
              "domainName": "corp.example",
              "enableSecret": "$9$hashed",
              "username": "admin",
              "usernameSecret": "$9$adminhash",
              "snmpCommunity": "n0t-public",
              "loggingHost": "10.99.0.5",
              "ntpServer": "192.0.2.123",
              "routerId": "10.0.0.1",
              "bgpAsn": "65001",
              "bgpNetworks": ["10.10.0.0/16"],
              "bgpRedistributeConnected": true,
              "vlans": [
                { "id": "10", "name": "users", "interfaceVlanIp": "192.168.10.1/24" },
                { "id": "20", "name": "servers", "interfaceVlanIp": "192.168.20.1/24" }
              ],
              "interfaces": [
                { "name": "GigabitEthernet0/0", "description": "Uplink", "mode": "routed", "ip": "203.0.113.1/30", "mtu": "1500" },
                { "name": "GigabitEthernet0/1", "mode": "access", "vlan": "10" },
                { "name": "GigabitEthernet0/2", "mode": "trunk", "allowedVlans": "10,20" }
              ],
              "ospfAreas": [
                { "cidr": "192.168.10.0/24", "area": "0" },
                { "cidr": "192.168.20.0/24", "area": "0" }
              ],
              "bgpNeighbors": [
                { "peerIp": "203.0.113.2", "remoteAs": "64512", "description": "Transit" }
              ],
              "acls": [
                { "name": "MGMT-IN", "action": "permit", "protocol": "tcp", "source": "10.0.0.0/8", "destination": "any", "destinationPort": "22" },
                { "name": "MGMT-IN", "action": "deny", "protocol": "tcp", "source": "any", "destination": "any", "destinationPort": "23", "log": true }
              ],
              "nat": { "inside": ["GigabitEthernet0/1"], "outside": "GigabitEthernet0/0", "aclName": "NAT-ACL" }
            }
            """;

        public ToolsPage()
        {
            this.InitializeComponent();
            GeneratorSpec.Text = SampleDeviceSpec;
            GeneratorPlatform.SelectedIndex = 0;
            TranslateDirection.SelectedIndex = 0;
            PlaybookScenario.SelectedIndex = 0;
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string header && !string.IsNullOrWhiteSpace(header))
            {
                SelectTab(header);
            }
        }

        private void SelectTab(string header)
        {
            for (int i = 0; i < ToolsTabs.TabItems.Count; i++)
            {
                if (ToolsTabs.TabItems[i] is TabViewItem { Header: string h } && h == header)
                {
                    ToolsTabs.SelectedIndex = i;
                    return;
                }
            }
        }

        private sealed class FindingItem
        {
            public required int Line { get; init; }
            public required string Severity { get; init; }
            public required string RuleId { get; init; }
            public required string Description { get; init; }
            public required SolidColorBrush SeverityBrush { get; init; }
        }

        private static SolidColorBrush SeverityBrush(string severity)
        {
            return severity.ToLowerInvariant() switch
            {
                "critical" or "error" or "danger" => Brush("AppDangerBrush"),
                "warning" => Brush("AppWarningBrush"),
                _ => Brush("AppBorderBrush"),
            };
        }

        private static SolidColorBrush Brush(string key)
        {
            if (Application.Current.Resources.TryGetValue(key, out object value) && value is SolidColorBrush brush)
            {
                return brush;
            }

            return new SolidColorBrush(Colors.Gray);
        }

        private static void ShowCode(CodeBlockView view, string title, string text)
        {
            view.DataContext = new ChatMessage { IsCode = true, CodeTitle = title, Text = text };
            view.Visibility = Visibility.Visible;
        }

        private void ShowFindings(ListView list, string caption, IEnumerable<FindingItem> items)
        {
            list.ItemsSource = items.ToList();
            list.Visibility = items.Any() ? Visibility.Visible : Visibility.Collapsed;
            if (!items.Any())
            {
                Toaster.Show(caption);
            }
        }

        // ===================== AI-assisted tools =====================

        /// <summary>
        /// Runs a tool-specific AI request against the selected model and streams
        /// the response into the given code view. Guards on a selected model,
        /// disables the invoking button while streaming, and surfaces failures
        /// via toast. Returns false when the request never reached a model.
        /// </summary>
        private async Task<bool> RunAiAsync(Button button, CodeBlockView view, string title, string systemPrompt, string content)
        {
            if (!ChatService.IsModelSelected)
            {
                Toaster.Show("No model selected. Refresh models in the Assistant panel or Settings.", InfoBarSeverity.Warning, "Model required");
                return false;
            }

            var message = new ChatMessage
            {
                IsCode = true,
                CodeTitle = title,
                Text = "",
                IsStreaming = true,
                Provider = AppSettings.SelectedProvider,
                Model = AppSettings.SelectedModel,
            };
            view.DataContext = message;
            view.Visibility = Visibility.Visible;

            button.IsEnabled = false;
            try
            {
                await foreach (var token in ChatService.StreamAsync(content, systemPrompt))
                {
                    message.Text += token;
                }

                return true;
            }
            catch (Exception ex)
            {
                message.Text = string.IsNullOrWhiteSpace(message.Text)
                    ? ex.Message
                    : message.Text + "\n\n" + ex.Message;
                Toaster.Show(ex.Message, InfoBarSeverity.Error, "AI request failed");
                return false;
            }
            finally
            {
                message.IsStreaming = false;
                button.IsEnabled = true;
            }
        }

        // ===================== IP Calculator =====================

        private void IpCalculate_Click(object sender, RoutedEventArgs e)
        {
            var input = IpInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                Toaster.Show("Enter a CIDR like 192.168.10.0/24", InfoBarSeverity.Warning);
                return;
            }

            try
            {
                var s = IpToolkit.Calculate(input);
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

                ShowCode(IpResult, "Subnet Information", sb.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                Toaster.Show(ex.Message, InfoBarSeverity.Error, "Invalid CIDR");
            }
        }

        // ===================== Config Generator =====================

        private void GeneratorGenerate_Click(object sender, RoutedEventArgs e)
        {
            DeviceSpec? spec;
            try
            {
                spec = JsonSerializer.Deserialize<DeviceSpec>(GeneratorSpec.Text, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            }
            catch (JsonException ex)
            {
                Toaster.Show(ex.Message, InfoBarSeverity.Error, "Invalid device spec JSON");
                return;
            }

            if (spec is null)
            {
                Toaster.Show("The device spec is empty.", InfoBarSeverity.Warning);
                return;
            }

            var platform = (ConfigPlatform)GeneratorPlatform.SelectedIndex;
            var config = ConfigGenerator.Generate(platform, spec);
            ShowCode(GeneratorResult, $"{platform} Configuration", config);
        }

        // ===================== Config Audit =====================

        private void AuditRun_Click(object sender, RoutedEventArgs e)
        {
            var findings = ConfigAuditor.Audit(AuditInput.Text);
            ShowFindings(AuditFindings, "No issues found.", findings.Select(f => new FindingItem
            {
                Line = f.LineNumber,
                Severity = f.Severity.ToString(),
                RuleId = f.RuleId,
                Description = f.Title,
                SeverityBrush = SeverityBrush(f.Severity.ToString()),
            }));
        }

        private async void AuditAi_Click(object sender, RoutedEventArgs e)
        {
            var text = AuditInput.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                Toaster.Show("Paste a configuration to analyze.", InfoBarSeverity.Warning);
                return;
            }

            var findings = ConfigAuditor.Audit(text);
            var sb = new StringBuilder();
            foreach (var f in findings)
            {
                sb.AppendLine($"[{f.Severity}] line {f.LineNumber} {f.RuleId}: {f.Title}");
            }

            var content = $"Configuration:\n{text}\n\nRule-based findings:\n{(findings.Count == 0 ? "none" : sb.ToString().TrimEnd())}";
            await RunAiAsync(AuditAiButton, AuditAiResult, "AI Audit Assessment", ToolPrompts.ConfigAudit, content);
        }

        // ===================== Diff =====================

        private void DiffRun_Click(object sender, RoutedEventArgs e)
        {
            var oldText = DiffOldInput.Text ?? string.Empty;
            var newText = DiffNewInput.Text ?? string.Empty;

            if (oldText.Length == 0 && newText.Length == 0)
            {
                Toaster.Show("Paste two configurations to compare.", InfoBarSeverity.Warning);
                return;
            }

            var diff = TextDiff.ToUnified(TextDiff.DiffLines(oldText, newText));
            ShowCode(DiffResult, "Configuration Diff", diff);
        }

        private async void DiffAi_Click(object sender, RoutedEventArgs e)
        {
            var oldText = DiffOldInput.Text ?? string.Empty;
            var newText = DiffNewInput.Text ?? string.Empty;

            if (oldText.Length == 0 && newText.Length == 0)
            {
                Toaster.Show("Paste two configurations to analyze.", InfoBarSeverity.Warning);
                return;
            }

            var diff = TextDiff.ToUnified(TextDiff.DiffLines(oldText, newText));
            var content = $"Baseline configuration:\n{oldText}\n\nCandidate configuration:\n{newText}\n\nUnified diff:\n{diff}";
            await RunAiAsync(DiffAiButton, DiffAiResult, "AI Diff Analysis", ToolPrompts.ConfigDiff, content);
        }

        // ===================== Log Analyzer =====================

        private void LogAnalyze_Click(object sender, RoutedEventArgs e)
        {
            var lines = (LogInput.Text ?? string.Empty).Split('\n');
            var analysis = LogAnalyzer.Analyze(lines);

            if (analysis.Entries.Count == 0)
            {
                Toaster.Show("No log lines to analyze.", InfoBarSeverity.Warning);
                return;
            }

            ShowFindings(LogFindings, "No anomalies detected.", analysis.Findings.Select(f => new FindingItem
            {
                Line = f.LineNumber,
                Severity = f.Severity.ToString(),
                RuleId = f.RuleId,
                Description = f.Description,
                SeverityBrush = SeverityBrush(f.Severity.ToString()),
            }));
        }

        private async void LogAi_Click(object sender, RoutedEventArgs e)
        {
            var text = LogInput.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                Toaster.Show("Paste log lines to analyze.", InfoBarSeverity.Warning);
                return;
            }

            var analysis = LogAnalyzer.Analyze(text.Split('\n'));
            var sb = new StringBuilder();
            foreach (var f in analysis.Findings)
            {
                sb.AppendLine($"[{f.Severity}] line {f.LineNumber} {f.RuleId}: {f.Description}");
            }

            var content = $"Log lines:\n{text}\n\nRule-based findings:\n{(analysis.Findings.Count == 0 ? "none" : sb.ToString().TrimEnd())}";
            await RunAiAsync(LogAiButton, LogAiResult, "AI Log Analysis", ToolPrompts.LogAnalysis, content);
        }

        // ===================== Playbooks =====================

        private void PlaybookScenario_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaybookResult is null)
            {
                return;
            }

            GeneratePlaybook();
        }

        private void PlaybookGenerate_Click(object sender, RoutedEventArgs e)
        {
            GeneratePlaybook();
        }

        private void GeneratePlaybook()
        {
            var scenario = ScenarioKey(PlaybookScenario.SelectedIndex);
            var playbook = PlaybookGenerator.Generate(scenario);
            ShowCode(PlaybookResult, $"{scenario} playbook", PlaybookGenerator.RenderPlain(playbook));
        }

        private async void PlaybookAi_Click(object sender, RoutedEventArgs e)
        {
            var scenario = ScenarioKey(PlaybookScenario.SelectedIndex);
            var display = (PlaybookScenario.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? scenario;
            var content = $"Scenario: {scenario} ({display}).\n\nWrite a step-by-step playbook for this scenario.";
            await RunAiAsync(PlaybookAiButton, PlaybookAiResult, $"{scenario} playbook (AI)", ToolPrompts.Playbook, content);
        }

        private static string ScenarioKey(int index) => index switch
        {
            1 => "bgp-flap",
            2 => "high-cpu",
            3 => "interface-down",
            4 => "ospf-adjacency",
            5 => "security-hardening",
            _ => "new-switch",
        };

        // ===================== Topology =====================

        private void TopologyBuild_Click(object sender, RoutedEventArgs e)
        {
            var text = TopologyInput.Text ?? string.Empty;
            var configs = ParseDeviceConfigs(text);

            if (configs.Count == 0)
            {
                Toaster.Show("Paste at least one device configuration.", InfoBarSeverity.Warning);
                return;
            }

            var topology = TopologyBuilder.Build(configs);
            var nodes = topology.Nodes.Count;
            var external = topology.Nodes.Count(n => n.Kind == "external");
            var mermaid = TopologyBuilder.RenderMermaid(topology);

            TopologySummary.Text = $"{nodes - external} devices, {external} external peers, {topology.Links.Count} links (rendered as Mermaid, e.g. in mermaid.live).";
            TopologySummary.Visibility = Visibility.Visible;
            ShowCode(TopologyResult, "Topology (Mermaid)", mermaid);
        }

        private async void TopologyAi_Click(object sender, RoutedEventArgs e)
        {
            var text = TopologyInput.Text ?? string.Empty;
            var configs = ParseDeviceConfigs(text);

            if (configs.Count == 0)
            {
                Toaster.Show("Paste device configurations to analyze.", InfoBarSeverity.Warning);
                return;
            }

            var topology = TopologyBuilder.Build(configs);
            var mermaid = TopologyBuilder.RenderMermaid(topology);
            var content = $"Device configurations:\n{text}\n\nInferred topology (Mermaid):\n{mermaid}";
            await RunAiAsync(TopologyAiButton, TopologyAiResult, "AI Topology Overview", ToolPrompts.Topology, content);
        }

        private static List<DeviceConfig> ParseDeviceConfigs(string text)
        {
            var result = new List<DeviceConfig>();
            var blocks = text.Split(new[] { "\r\n==== ", "==== ", "===" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                var name = block.Split('\n', 2)[0].Trim();
                var body = block.Contains('\n') ? block.Split('\n', 2)[1] : string.Empty;
                if (body.Length > 0)
                {
                    result.Add(new DeviceConfig(name.Trim(), body));
                }
            }

            return result;
        }

        // ===================== Translator =====================

        private void TranslateRun_Click(object sender, RoutedEventArgs e)
        {
            var input = TranslateInput.Text ?? string.Empty;
            if (input.Length == 0)
            {
                Toaster.Show("Paste a configuration to translate.", InfoBarSeverity.Warning);
                return;
            }

            var iosToJunos = TranslateDirection.SelectedIndex == 0;
            var output = iosToJunos
                ? ConfigTranslator.IosToJunos(input)
                : ConfigTranslator.JunosToIos(input);

            ShowCode(TranslateResult, iosToJunos ? "Juniper Junos (set)" : "Cisco IOS-XE", output);
        }

        private async void TranslateAi_Click(object sender, RoutedEventArgs e)
        {
            var input = TranslateInput.Text ?? string.Empty;
            if (input.Length == 0)
            {
                Toaster.Show("Paste a configuration to translate.", InfoBarSeverity.Warning);
                return;
            }

            var iosToJunos = TranslateDirection.SelectedIndex == 0;
            var target = iosToJunos ? "Juniper Junos (set format)" : "Cisco IOS-XE";
            var content = $"Translate the following configuration to {target}:\n\n{input}";
            await RunAiAsync(TranslateAiButton, TranslateAiResult, $"AI Translation ({target})", ToolPrompts.Translation, content);
        }
    }
}

