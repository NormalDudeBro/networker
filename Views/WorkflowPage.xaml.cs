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
using Microsoft.Extensions.DependencyInjection;
using Networker.Core.NetTools.Config;
using Networker.Core.NetTools.Ip;
using Networker.Core.NetTools.Logs;
using Networker.Core.NetTools.Playbooks;
using Networker.Core.NetTools.Topology;
using Networker.Core.Prompting;
using networker.Controls;
using networker.Models;
using networker.Services;
using Networker.Core.Workflow;

namespace networker.Views
{
    public sealed partial class WorkflowPage : Page
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

        private readonly Dictionary<string, FrameworkElement> _workflowPanels;
        private FrameworkElement? _activeWorkflow;
        private bool _isSelectingWorkflow;
        private bool _isLlmSubscribed;
        private WorkflowStage _activeStage = WorkflowStage.Inspect;
        private readonly TroubleshootingSession _session;
        private bool _workspaceRestored;

        public WorkflowPage()
        {
            this.InitializeComponent();
            _session = ((App)Application.Current).Services.GetRequiredService<TroubleshootingSession>();
            GeneratorSpec.Text = SampleDeviceSpec;
            GeneratorPlatform.SelectedIndex = 0;
            TranslateDirection.SelectedIndex = 0;
            PlaybookScenario.SelectedIndex = 0;

            _workflowPanels = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ip"] = IpToolPanel,
                ["json-generator"] = JsonGeneratorPanel,
                ["config-audit"] = AuditToolPanel,
                ["log-analyzer"] = LogAnalyzerPanel,
                ["playbooks"] = PlaybooksPanel,
                ["topology"] = TopologyPanel,
                ["translator"] = TranslatorPanel,
                ["config-generate"] = ConfigGeneratePanel,
                ["config-import"] = ConfigImportPanel,
                ["config-diff"] = ConfigDiffPanel,
            };

            StageToolSelector.ItemsSource = ToolDescriptor.All;

            SelectStage(WorkflowStage.Inspect, AppSettings.SelectedToolKey);

            ConfigImportControl.WorkspaceChanged += CaptureWorkspace;
            ConfigImportControl.ActionCompleted += message => CompleteStage(WorkflowStage.Inspect, message);
            ConfigImportControl.ActionFailed += message => FailStage(WorkflowStage.Inspect, message);
            ConfigDiffControl.WorkspaceChanged += CaptureWorkspace;
            ConfigDiffControl.ActionCompleted += message => CompleteStage(WorkflowStage.Compare, message);
            ConfigDiffControl.ActionFailed += message => FailStage(WorkflowStage.Compare, message);
            ConfigGenerateControl.WorkspaceChanged += CaptureWorkspace;
            ConfigGenerateControl.ActionCompleted += message => CompleteStage(WorkflowStage.Resolve, message);
            ConfigGenerateControl.ActionFailed += message => FailStage(WorkflowStage.Resolve, message);
            RestoreWorkspace();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (!_isLlmSubscribed)
            {
                LlmSession.Changed += LlmSession_Changed;
                _isLlmSubscribed = true;
            }
            UpdateAiAvailability();

            if (e.Parameter is string value && !string.IsNullOrWhiteSpace(value))
            {
                if (WorkflowStageCatalog.TryFind(value, out var definition))
                {
                    SelectStage(definition.Stage);
                }
                else
                {
                    SelectWorkflow(value);
                }
            }
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_isLlmSubscribed)
            {
                LlmSession.Changed -= LlmSession_Changed;
                _isLlmSubscribed = false;
            }
        }

        public void SelectStage(WorkflowStage stage, string? toolKey = null)
        {
            if (stage < WorkflowStage.Inspect || stage > WorkflowStage.Resolve) return;
            _activeStage = stage;

            var tools = ToolDescriptor.All.Where(tool => StageForTool(tool.Key) == stage).ToList();
            StageToolSelector.ItemsSource = tools;
            StageToolSelector.Visibility = tools.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            var definition = WorkflowStageCatalog.Get(stage);
            StageEyebrow.Text = $"STEP {definition.Number} OF 9";
            StageTitle.Text = definition.Label;
            StageSubtitle.Text = definition.Description;

            string defaultKey = stage switch
            {
                WorkflowStage.Inspect => "config-import",
                WorkflowStage.Diagnose => "config-audit",
                WorkflowStage.Map => "topology",
                WorkflowStage.Compare => "config-diff",
                WorkflowStage.Plan => "playbooks",
                WorkflowStage.Resolve => "config-generate",
                _ => "config-import",
            };
            string selectedKey = toolKey is not null && tools.Any(t => t.Matches(toolKey)) ? toolKey : defaultKey;
            SelectWorkflow(selectedKey);
        }

        private static WorkflowStage StageForTool(string key) => key switch
        {
            "config-import" => WorkflowStage.Inspect,
            "config-audit" or "log-analyzer" => WorkflowStage.Diagnose,
            "topology" or "ip" => WorkflowStage.Map,
            "quick-diff" or "config-diff" => WorkflowStage.Compare,
            "playbooks" => WorkflowStage.Plan,
            "config-generate" or "translator" or "json-generator" => WorkflowStage.Resolve,
            _ => WorkflowStage.Settings,
        };

        public void SelectWorkflow(string header)
        {
            if (header.Equals("quick-diff", StringComparison.OrdinalIgnoreCase)) header = "config-diff";
            var descriptor = ToolDescriptor.Find(header);
            if (descriptor is null || !_workflowPanels.TryGetValue(descriptor.Key, out var panel))
            {
                return;
            }

            var targetStage = StageForTool(descriptor.Key);
            if (targetStage is >= WorkflowStage.Inspect and <= WorkflowStage.Resolve && targetStage != _activeStage)
            {
                SelectStage(targetStage, descriptor.Key);
                return;
            }

            if (!ReferenceEquals(_activeWorkflow, panel))
            {
                if (_activeWorkflow is not null) _activeWorkflow.Visibility = Visibility.Collapsed;
                panel.Visibility = Visibility.Visible;
                _activeWorkflow = panel;
            }

            _isSelectingWorkflow = true;
            try
            {
                StageToolSelector.SelectedItem = descriptor;
            }
            finally
            {
                _isSelectingWorkflow = false;
            }

            AppSettings.SelectedToolKey = descriptor.Key;
        }

        private void StageToolSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSelectingWorkflow && StageToolSelector.SelectedItem is ToolDescriptor descriptor)
            {
                SelectWorkflow(descriptor.Key);
            }
        }

        private void LlmSession_Changed() => DispatcherQueue.TryEnqueue(UpdateAiAvailability);

        private void UpdateAiAvailability()
        {
            bool available = ChatService.IsModelSelected;
            foreach (var button in new[] { AuditAiButton, LogAiButton, PlaybookAiButton, TopologyAiButton, TranslateAiButton })
            {
                button.IsEnabled = available;
                ToolTipService.SetToolTip(button, available ? null : "Select an AI model in Assistant or Settings.");
            }

            ToolsAiStatusDot.Fill = (Brush)Application.Current.Resources[available ? "AppOnlineBrush" : "AppOfflineBrush"];
            ToolsAiStatusText.Text = available
                ? $"AI ready · {AppSettings.SelectedModel}"
                : "AI model unavailable";
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
                _ => Brush("AppTextSecondaryBrush"),
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

        private static void LogActivity(string title, string detail, string glyph = "\uE774")
        {
            string text = (detail ?? "").Trim();
            RecentActivity.Add(new ActivityItem
            {
                Title = title,
                Detail = text.Length <= 200 ? text : text[..200] + "…",
                Timestamp = DateTime.Now,
                Glyph = glyph
            });
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
                FailStage(WorkflowStage.Map, "Enter a valid CIDR to calculate.");
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

                var text = sb.ToString().TrimEnd();
                ShowCode(IpResult, "Subnet Information", text);
                LogActivity("Subnet Calculator", text, "\uE774");
                SaveNamed("map.ip.input", IpInput.Text);
                SaveNamed("map.ip.result", text);
                CompleteStage(WorkflowStage.Map, "Subnet calculation complete.");
            }
            catch (Exception ex)
            {
                Toaster.Show(ex.Message, InfoBarSeverity.Error, "Invalid CIDR");
                FailStage(WorkflowStage.Map, ex.Message);
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
            LogActivity($"{platform} Config Generator", config, "\uE943");
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
            LogActivity("Config Audit", $"{findings.Count} finding(s)", "\uE8FD");
            SaveNamed("diagnose.audit.input", AuditInput.Text);
            SaveNamed("diagnose.audit.summary", $"{findings.Count} finding(s)");
            CompleteStage(WorkflowStage.Diagnose, $"Configuration audit found {findings.Count} item(s).");
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

        // ===================== Log Analyzer =====================

        private void LogAnalyze_Click(object sender, RoutedEventArgs e)
        {
            var lines = (LogInput.Text ?? string.Empty).Split('\n');
            var analysis = LogAnalyzer.Analyze(lines);

            if (analysis.Entries.Count == 0)
            {
                Toaster.Show("No log lines to analyze.", InfoBarSeverity.Warning);
                FailStage(WorkflowStage.Diagnose, "Paste log lines to analyze.");
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
            LogActivity("Log Analysis", $"{analysis.Findings.Count} finding(s) in {analysis.Entries.Count} lines", "\uE721");
            SaveNamed("diagnose.logs.input", LogInput.Text);
            SaveNamed("diagnose.logs.summary", $"{analysis.Findings.Count} finding(s) in {analysis.Entries.Count} lines");
            CompleteStage(WorkflowStage.Diagnose, "Log analysis complete.");
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

        private void PlaybookGenerate_Click(object sender, RoutedEventArgs e)
        {
            GeneratePlaybook();
        }

        private void GeneratePlaybook()
        {
            var scenario = ScenarioKey(PlaybookScenario.SelectedIndex);
            var playbook = PlaybookGenerator.Generate(scenario);
            var rendered = PlaybookGenerator.RenderPlain(playbook);
            ShowCode(PlaybookResult, $"{scenario} playbook", rendered);
            LogActivity($"{scenario} Playbook", rendered, "\uE8A5");
            SaveNamed("plan.scenario", scenario);
            SaveNamed("plan.result", rendered);
            CompleteStage(WorkflowStage.Plan, "Troubleshooting playbook generated.");
        }

        private async void PlaybookAi_Click(object sender, RoutedEventArgs e)
        {
            var scenario = ScenarioKey(PlaybookScenario.SelectedIndex);
            var display = (PlaybookScenario.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? scenario;
            var customScenario = (PlaybookCustomScenario.Text ?? string.Empty).Trim();
            var content = customScenario.Length > 0
                ? $"User-defined network scenario:\n{customScenario}\n\nWrite a step-by-step operational playbook for this scenario."
                : $"Scenario: {scenario} ({display}).\n\nWrite a step-by-step operational playbook for this scenario.";
            var title = customScenario.Length > 0 ? "Custom playbook (AI)" : $"{scenario} playbook (AI)";

            await RunAiAsync(PlaybookAiButton, PlaybookAiResult, title, ToolPrompts.Playbook, content);
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
                FailStage(WorkflowStage.Map, "Paste at least one device configuration.");
                return;
            }

            var topology = TopologyBuilder.Build(configs);
            var nodes = topology.Nodes.Count;
            var external = topology.Nodes.Count(n => n.Kind == "external");
            var mermaid = TopologyBuilder.RenderMermaid(topology);

            TopologySummary.Text = $"{nodes - external} devices, {external} external peers, {topology.Links.Count} links (rendered as Mermaid, e.g. in mermaid.live).";
            TopologySummary.Visibility = Visibility.Visible;
            ShowCode(TopologyResult, "Topology (Mermaid)", mermaid);
            LogActivity("Topology", TopologySummary.Text, "\uE703");
            SaveNamed("map.topology.input", TopologyInput.Text);
            SaveNamed("map.topology.result", mermaid);
            CompleteStage(WorkflowStage.Map, "Topology inferred.");
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
                FailStage(WorkflowStage.Resolve, "Paste a configuration to translate.");
                return;
            }

            var iosToJunos = TranslateDirection.SelectedIndex == 0;
            var output = iosToJunos
                ? ConfigTranslator.IosToJunos(input)
                : ConfigTranslator.JunosToIos(input);

            ShowCode(TranslateResult, iosToJunos ? "Juniper Junos (set)" : "Cisco IOS-XE", output);
            LogActivity("Config Translation", output, "\uE8D4");
            SaveNamed("resolve.translate.input", TranslateInput.Text);
            SaveNamed("resolve.translate.result", output);
            CompleteStage(WorkflowStage.Resolve, "Configuration translated.");
        }

        private void RestoreWorkspace()
        {
            if (_workspaceRestored) return;
            _workspaceRestored = true;
            var values = _session.Current.NamedValues;
            IpInput.Text = Get(values, "map.ip.input");
            AuditInput.Text = Get(values, "diagnose.audit.input");
            LogInput.Text = Get(values, "diagnose.logs.input");
            TopologyInput.Text = Get(values, "map.topology.input");
            TranslateInput.Text = Get(values, "resolve.translate.input");
            PlaybookCustomScenario.Text = Get(values, "plan.custom");
            ConfigImportControl.RestoreState(Get(values, "inspect.input"), Get(values, "inspect.result"));
            ConfigDiffControl.RestoreState(Get(values, "compare.baseline"), Get(values, "compare.candidate"), Get(values, "compare.result"), Get(values, "compare.stats"));
            ConfigGenerateControl.RestoreState(_session.Current.Generate);
        }

        private static string Get(IReadOnlyDictionary<string, string> values, string key)
            => values.TryGetValue(key, out string? value) ? value : string.Empty;

        private void CaptureWorkspace()
        {
            var import = ConfigImportControl.CaptureState();
            var diff = ConfigDiffControl.CaptureState();
            SaveNamed("inspect.input", import.Input, notify: false);
            SaveNamed("inspect.result", import.Results, notify: false);
            SaveNamed("compare.baseline", diff.Baseline, notify: false);
            SaveNamed("compare.candidate", diff.Candidate, notify: false);
            SaveNamed("compare.result", diff.Results, notify: false);
            SaveNamed("compare.stats", diff.Stats, notify: false);
            _session.Current.Generate = ConfigGenerateControl.CaptureState();
            _session.NotifyChanged();
        }

        private void SaveNamed(string key, string? value, bool notify = true)
        {
            _session.Current.NamedValues[key] = value ?? string.Empty;
            if (notify) _session.NotifyChanged();
        }

        private void CompleteStage(WorkflowStage stage, string message)
        {
            CaptureWorkspace();
            _session.SetCompleted(stage, message);
        }

        private void FailStage(WorkflowStage stage, string message) => _session.SetError(stage, message);

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

