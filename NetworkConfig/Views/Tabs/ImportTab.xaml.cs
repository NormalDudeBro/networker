using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using networker.Models;
using networker.Services;
using Networker.Core.Models.NetworkConfig;
using Networker.Core.Services.NetworkConfig;
using Windows.Storage;

namespace networker.NetworkConfig.Views.Tabs
{
    /// <summary>
    /// Import / Analyze tab — paste a configuration (or a syslog file) and run
    /// it through the shared parser + validator. Ported from NetworkConfigPro's
    /// Import tab (<c>_parse_config</c>, <c>_import_syslog_file</c>).
    /// </summary>
    public sealed partial class ImportTab : UserControl
    {
        private static readonly string[] SyslogSeverities =
            { "EMERG", "ALERT", "CRIT", "ERR", "WARNING", "NOTICE", "INFO", "DEBUG" };

        private readonly IConfigParserFactory _parserFactory;
        private readonly IConfigValidator _validator;

        public ImportTab()
        {
            this.InitializeComponent();

            var services = ((App)Application.Current).Services;
            _parserFactory = services.GetService<IConfigParserFactory>()
                ?? throw new InvalidOperationException("IConfigParserFactory is not registered in the DI container.");
            _validator = services.GetService<IConfigValidator>()
                ?? throw new InvalidOperationException("IConfigValidator is not registered in the DI container.");
        }

        private void Parse_Click(object sender, RoutedEventArgs e)
        {
            var configText = ImportText.Text.Trim();
            if (configText.Length == 0)
            {
                SetStatus("Please paste a configuration first", error: true);
                return;
            }

            var parser = _parserFactory.GetParser(configText);
            if (parser is null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("ERRORS:");
                sb.AppendLine("  - Could not detect configuration vendor/format");
                ResultsText.Text = sb.ToString();
                SetStatus("Parse errors occurred", error: true);
                return;
            }

            var result = parser.Parse(configText);
            if (result.Errors.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("ERRORS:");
                foreach (var error in result.Errors)
                {
                    sb.AppendLine($"  - {error}");
                }

                ResultsText.Text = sb.ToString();
                SetStatus("Parse errors occurred", error: true);
                return;
            }

            var config = result.Config;
            if (config is null)
            {
                SetStatus("Parse error: no configuration was produced", error: true);
                return;
            }

            ResultsText.Text = FormatResults(result, config);
            SetStatus($"Parsed: {config.Hostname}");
            LogActivity("Config Parse", $"{config.Hostname} — {result.Vendor}", "\uE774");
        }

        private async void ImportSyslogFile_Click(object sender, RoutedEventArgs e)
        {
            var window = MainWindow.Instance;
            if (window is null)
            {
                return;
            }

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".log");
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(window));

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            try
            {
                var content = await FileIO.ReadTextAsync(file);
                ImportText.Text = content;
                ShowSyslogSummary(content, file.Name);
                SetStatus($"Loaded syslog file: {file.Name}");
                LogActivity("Syslog Import", file.Name, "\uE774");
            }
            catch (Exception ex)
            {
                SetStatus($"Error loading syslog file: {ex.Message}", error: true);
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ImportText.Text = string.Empty;
            ResultsText.Text = string.Empty;
            SetStatus(string.Empty);
        }

        /// <summary>
        /// Builds the parse-results report, mirroring Python's <c>_parse_config</c>
        /// output line-for-line (hostname/vendor/domain, interface + VLAN previews,
        /// routing summary, warnings, and the first 10 validation issues).
        /// </summary>
        private string FormatResults(ParseResult result, NetworkDeviceConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PARSED CONFIGURATION");
            sb.AppendLine(new string('=', 40));
            sb.AppendLine($"Hostname: {config.Hostname}");
            sb.AppendLine($"Vendor: {(result.Vendor?.ToString() ?? "Unknown")}");
            sb.AppendLine($"Domain: {config.DomainName ?? "Not set"}");
            sb.AppendLine();
            sb.AppendLine($"Interfaces: {config.Interfaces.Count}");
            foreach (var iface in config.Interfaces.Take(5))
            {
                sb.AppendLine($"  - {iface.Name}: {iface.IpAddress?.ToString() ?? "no IP"}");
            }

            if (config.Interfaces.Count > 5)
            {
                sb.AppendLine($"  ... and {config.Interfaces.Count - 5} more");
            }

            sb.AppendLine();
            sb.AppendLine($"VLANs: {config.Vlans.Count}");
            foreach (var vlan in config.Vlans.Take(5))
            {
                sb.AppendLine($"  - VLAN {vlan.VlanId}: {vlan.Name}");
            }

            if (config.Vlans.Count > 5)
            {
                sb.AppendLine($"  ... and {config.Vlans.Count - 5} more");
            }

            sb.AppendLine();
            sb.AppendLine($"Static Routes: {config.StaticRoutes.Count}");
            sb.AppendLine($"OSPF: {(config.Ospf is not null ? "Configured" : "Not configured")}");
            sb.AppendLine($"BGP: {(config.Bgp is not null ? "Configured" : "Not configured")}");

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("WARNINGS:");
                foreach (var warning in result.Warnings)
                {
                    sb.AppendLine($"  - {warning}");
                }
            }

            var issues = _validator.Validate(config);
            if (issues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"VALIDATION ({issues.Count} issues):");
                foreach (var issue in issues.Take(10))
                {
                    var severity = issue.Severity switch
                    {
                        ValidationSeverity.Error => "error",
                        ValidationSeverity.Warning => "warning",
                        _ => "info",
                    };
                    sb.AppendLine($"  [{severity}] {issue.Message}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Syslog severity-count summary, mirroring Python's <c>_import_syslog_file</c>.
        /// </summary>
        private void ShowSyslogSummary(string content, string fileName)
        {
            var lines = string.IsNullOrEmpty(content)
                ? Array.Empty<string>()
                : content.Replace("\r\n", "\n").Split('\n');

            var counts = new Dictionary<string, int>();
            foreach (var severity in SyslogSeverities)
            {
                counts[severity] = 0;
            }

            var totalParsed = 0;
            foreach (var line in lines)
            {
                var upper = line.ToUpperInvariant();
                var matched = false;
                foreach (var severity in SyslogSeverities)
                {
                    if (upper.Contains(severity, StringComparison.Ordinal))
                    {
                        counts[severity]++;
                        matched = true;
                    }
                }

                if (matched)
                {
                    totalParsed++;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("SYSLOG FILE IMPORTED");
            sb.AppendLine(new string('=', 40));
            sb.AppendLine($"File: {fileName}");
            sb.AppendLine($"Total lines: {lines.Length}");
            sb.AppendLine();
            sb.AppendLine("SYSLOG SUMMARY");
            sb.AppendLine(new string('-', 40));
            sb.AppendLine("Message Severity Counts:");
            foreach (var severity in SyslogSeverities)
            {
                if (counts[severity] > 0)
                {
                    sb.AppendLine($"  {severity}: {counts[severity]}");
                }
            }

            sb.AppendLine($"Parsed entries: {totalParsed}");
            sb.AppendLine($"Unparsed lines: {lines.Length - totalParsed}");
            sb.AppendLine();
            sb.AppendLine("TIP: Severity counts come from simple token matching. For structured");
            sb.AppendLine("results, clear this box, paste a full device configuration, and use");
            sb.AppendLine("'Parse Configuration'.");

            ResultsText.Text = sb.ToString();
        }

        private void SetStatus(string message, bool error = false)
        {
            StatusText.Text = message;
            StatusText.Foreground = error
                ? (Brush)Application.Current.Resources["AppDangerBrush"]
                : (Brush)Application.Current.Resources["AppTextSecondaryBrush"];
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
}
