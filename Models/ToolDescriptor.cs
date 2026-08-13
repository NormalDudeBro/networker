using System;
using System.Collections.Generic;
using System.Linq;

namespace networker.Models
{
    /// <summary>Stable metadata shared by Tools, Dashboard, and command discovery.</summary>
    public sealed record ToolDescriptor(
        string Key,
        string Header,
        string Group,
        string Glyph,
        string Description,
        params string[] Aliases)
    {
        public string DisplayPath => Key switch
        {
            "config-import" => $"2 Inspect / {Header}",
            "config-audit" or "log-analyzer" => $"3 Diagnose / {Header}",
            "ip" or "topology" => $"4 Map / {Header}",
            "config-diff" => $"5 Compare / {Header}",
            "playbooks" => $"6 Plan / {Header}",
            "json-generator" or "translator" or "config-generate" => $"7 Resolve / {Header}",
            "vault" or "templates" => $"9 Settings / {Header}",
            _ => $"{Group} / {Header}",
        };

        public bool Matches(string value)
            => Key.Equals(value, StringComparison.OrdinalIgnoreCase)
               || Header.Equals(value, StringComparison.OrdinalIgnoreCase)
               || Aliases.Any(alias => alias.Equals(value, StringComparison.OrdinalIgnoreCase));

        public static IReadOnlyList<ToolDescriptor> All { get; } = new[]
        {
            new ToolDescriptor("ip", "IP Calculator", "Quick tools", "\uE94C", "CIDR, masks, host ranges, and address details", "subnet", "cidr"),
            new ToolDescriptor("json-generator", "JSON Generator", "Quick tools", "\uE943", "Generate a device configuration from an automation-friendly JSON spec", "Config Generator"),
            new ToolDescriptor("config-audit", "Config Audit", "Quick tools", "\uE8FD", "Scan configuration text for security and operational risks", "audit"),
            new ToolDescriptor("log-analyzer", "Log Analyzer", "Quick tools", "\uE721", "Find severity signals and recurring anomalies in network logs", "logs"),
            new ToolDescriptor("playbooks", "Playbooks", "Quick tools", "\uE8A5", "Build deterministic or AI-assisted operational runbooks", "runbook"),
            new ToolDescriptor("topology", "Topology", "Quick tools", "\uE703", "Infer a Mermaid topology from multiple device configurations", "diagram"),
            new ToolDescriptor("translator", "Translator", "Quick tools", "\uE8D7", "Translate supported configuration syntax between vendors", "translate"),
            new ToolDescriptor("config-generate", "Generate", "Configuration", "\uE943", "Guided six-vendor configuration generation and validation", "configuration workspace", "full generator"),
            new ToolDescriptor("config-import", "Import / Analyze", "Configuration", "\uE8B5", "Parse, identify, and validate an existing configuration", "import"),
            new ToolDescriptor("config-diff", "Config Diff", "Configuration", "\uE8C8", "Compare complete configuration revisions", "quick-diff", "compare", "workspace diff"),
            new ToolDescriptor("vault", "Vault", "Configuration", "\uE72E", "Manage encrypted credentials and reusable template variables", "secrets"),
            new ToolDescriptor("templates", "Templates", "Configuration", "\uE8A5", "Browse built-in and custom configuration templates", "template library"),
        };

        public static ToolDescriptor? Find(string keyOrAlias)
            => All.FirstOrDefault(tool => tool.Matches(keyOrAlias));
    }
}
