using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace networker.Services
{
    public enum HighlightType
    {
        Plain,
        Comment,
        Keyword,
        Ip,
        Number
    }

    public readonly record struct HighlightToken(string Text, HighlightType Type);

    /// <summary>
    /// Lightweight syntax tokenizer for network device configurations
    /// (Cisco IOS/EOS, Junos, VyOS). Deterministic presentation only.
    /// </summary>
    public static class ConfigSyntaxHighlighter
    {
        private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "hostname", "interface", "vlan", "router", "ip", "ipv6", "switchport",
            "neighbor", "network", "description", "no", "set", "security", "nat",
            "access-list", "route-map", "policy-map", "class-map", "service-policy",
            "banner", "line", "logging", "ntp", "snmp-server", "spanning-tree",
            "bgp", "ospf", "vrf", "mpls", "crypto", "dialer", "port", "channel-group",
            "etherchannel", "password", "username", "enable", "secret", "domain-name",
            "clock", "timezone", "service", "fhrp", "vrrp", "hsrp", "glbp",
            "interfaces", "system", "security", "protocols", "routing-options",
            "policy-options", "firewall", "nat", "interfaces", "static", "default"
        };

        private static readonly Regex IpRegex = new(@"\b(?:\d{1,3}\.){3}\d{1,3}(?:/\d{1,2})?\b", RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new(@"\b\d+\b", RegexOptions.Compiled);

        /// <summary>
        /// Tokenizes a single config line into colored spans.
        /// </summary>
        public static IReadOnlyList<HighlightToken> Tokenize(string line)
        {
            var tokens = new List<HighlightToken>();

            if (string.IsNullOrEmpty(line))
            {
                tokens.Add(new HighlightToken(line ?? "", HighlightType.Plain));
                return tokens;
            }

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("!") || trimmed.StartsWith("#") || trimmed.StartsWith(";") ||
                trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("* "))
            {
                tokens.Add(new HighlightToken(line, HighlightType.Comment));
                return tokens;
            }

            var words = Regex.Matches(line, @"\S+");
            for (int i = 0; i < words.Count; i++)
            {
                Match word = words[i];
                string text = word.Value;
                HighlightType type = HighlightType.Plain;

                if (IpRegex.IsMatch(text))
                {
                    type = HighlightType.Ip;
                }
                else if (NumberRegex.IsMatch(text))
                {
                    type = HighlightType.Number;
                }
                else if (Keywords.Contains(text))
                {
                    type = HighlightType.Keyword;
                }
                else if (i > 0 && (words[i - 1].Value.Equals("interface", System.StringComparison.OrdinalIgnoreCase) ||
                                   words[i - 1].Value.Equals("interfaces", System.StringComparison.OrdinalIgnoreCase)))
                {
                    type = HighlightType.Keyword;
                }

                tokens.Add(new HighlightToken(text, type));

                if (i < words.Count - 1)
                {
                    int gapStart = word.Index + word.Length;
                    int gapEnd = words[i + 1].Index;
                    if (gapEnd > gapStart)
                    {
                        tokens.Add(new HighlightToken(line.Substring(gapStart, gapEnd - gapStart), HighlightType.Plain));
                    }
                }
            }

            return tokens;
        }
    }
}
