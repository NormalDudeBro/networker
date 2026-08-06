using System.Text.RegularExpressions;

namespace Networker.Core.NetTools.Config;

public enum AuditSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public sealed class AuditFinding
{
    public required string RuleId { get; init; }
    public required AuditSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public int LineNumber { get; init; }
}

/// <summary>
/// Deterministic security and best-practice audit of a device configuration.
/// Pure pattern matching — no LLM involvement — so results are reproducible.
/// </summary>
public static class ConfigAuditor
{
    private sealed record LineRule(
        string Id,
        string Title,
        AuditSeverity Severity,
        Regex Pattern,
        string Message);

    private static readonly LineRule[] LineRules =
    {
        new("ENABLE_PASSWORD_PLAINTEXT", "Plaintext enable password",
            AuditSeverity.Error,
            new Regex(@"^\s*enable\s+password\s+\S+", RegexOptions.IgnoreCase),
            "Configure 'enable secret' (Type 8/9 hashed) instead of a plaintext 'enable password'."),

        new("TELNET_TRANSPORT", "Telnet permitted",
            AuditSeverity.Warning,
            new Regex(@"^\s*transport\s+input\s+(?:\w+\s+)*telnet\b", RegexOptions.IgnoreCase),
            "Telnet sends credentials in clear text. Restrict management access to SSH."),

        new("SNMP_PUBLIC_COMMUNITY", "Default SNMP community",
            AuditSeverity.Error,
            new Regex(@"^\s*snmp-server\s+community\s+(public|private)(\s|$)", RegexOptions.IgnoreCase),
            "SNMP is using the well-known default community. Use a strong, unique community string."),

        new("SNMP_RW_COMMUNITY", "SNMP read-write community",
            AuditSeverity.Warning,
            new Regex(@"^\s*snmp-server\s+community\s+\S+\s+rw\b", RegexOptions.IgnoreCase),
            "SNMP community has read-write access. Restrict to read-only or bind to an ACL."),

        new("SNMP_NO_ACL", "SNMP community without ACL",
            AuditSeverity.Warning,
            new Regex(@"^\s*snmp-server\s+community\s+(\S+)(?:\s+ro|\s+rw)?\s*$", RegexOptions.IgnoreCase),
            "SNMP community is not restricted by an access-list; any reachable host can query it."),

        new("HTTP_SERVER", "Insecure HTTP server",
            AuditSeverity.Warning,
            new Regex(@"^\s*ip\s+http\s+server\b", RegexOptions.IgnoreCase),
            "Plaintext HTTP management is enabled. Prefer HTTPS (ip http secure-server)."),

        new("IP_SOURCE_ROUTE", "IP source routing enabled",
            AuditSeverity.Warning,
            new Regex(@"^\s*ip\s+source-route\b", RegexOptions.IgnoreCase),
            "Source routing can be abused for traffic bypass. Disable it with 'no ip source-route'."),

        new("DIRECTED_BROADCAST", "Directed broadcast enabled",
            AuditSeverity.Warning,
            new Regex(@"^\s*ip\s+directed-broadcast\b", RegexOptions.IgnoreCase),
            "Directed broadcasts enable amplification (smurf) attacks. Disable them."),

        new("VTY_NO_SSH", "VTY not restricted to SSH",
            AuditSeverity.Warning,
            new Regex(@"^\s*transport\s+input\s+(?!.*ssh)\w+", RegexOptions.IgnoreCase),
            "A vty transport excludes SSH. Restrict management lines to 'transport input ssh'."),

        new("BOOTP_SERVER", "BOOTP server enabled",
            AuditSeverity.Info,
            new Regex(@"^\s*ip\s+bootp\s+server\b", RegexOptions.IgnoreCase),
            "BOOTP is rarely required; consider disabling if unused."),
    };

    private static readonly (string Id, string Title, AuditSeverity Severity, Func<string, bool> Check, string Message)[] ConfigRules =
    {
        ("PASSWORD_ENCRYPTION", "Service password encryption disabled",
            AuditSeverity.Warning,
            c => !Regex.IsMatch(c, @"^\s*service\s+password-encryption\b", RegexOptions.Multiline | RegexOptions.IgnoreCase),
            "Enable 'service password-encryption' so stored passwords are not plaintext."),

        ("ENABLE_SECRET_MISSING", "No enable secret configured",
            AuditSeverity.Error,
            c => !Regex.IsMatch(c, @"^\s*enable\s+secret\b", RegexOptions.Multiline | RegexOptions.IgnoreCase),
            "No 'enable secret' configured for privilege exec access."),

        ("LOGGING_NOT_CONFIGURED", "No logging configured",
            AuditSeverity.Info,
            c => !Regex.IsMatch(c, @"^\s*logging\s+", RegexOptions.Multiline | RegexOptions.IgnoreCase),
            "No syslog / buffered logging configured. Add 'logging buffered' or a syslog server."),

        ("DOMAIN_NAME_MISSING", "No domain name configured",
            AuditSeverity.Info,
            c => !Regex.IsMatch(c, @"^\s*ip\s+domain-name\b", RegexOptions.Multiline | RegexOptions.IgnoreCase),
            "No 'ip domain-name' configured; required for certificate-based services and FQDN lookups."),

        ("NTP_NOT_CONFIGURED", "No NTP configured",
            AuditSeverity.Info,
            c => !Regex.IsMatch(c, @"^\s*ntp\s+server\b", RegexOptions.Multiline | RegexOptions.IgnoreCase),
            "No NTP server configured; logs and certificates will use an unreliable clock."),

        ("SSH_VERSION_NOT_2", "SSH version not set to 2",
            AuditSeverity.Warning,
            c => Regex.IsMatch(c, @"^\s*transport\s+input\s+.*ssh", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                 && !Regex.IsMatch(c, @"^\s*ip\s+ssh\s+version\s+2\b", RegexOptions.Multiline | RegexOptions.IgnoreCase),
            "SSH is used for management but 'ip ssh version 2' is not set."),
    };

    public static IReadOnlyList<AuditFinding> Audit(string configText)
    {
        var findings = new List<AuditFinding>();

        if (string.IsNullOrWhiteSpace(configText))
        {
            return findings;
        }

        var lines = configText.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            foreach (var rule in LineRules)
            {
                if (rule.Pattern.IsMatch(line))
                {
                    findings.Add(new AuditFinding
                    {
                        RuleId = rule.Id,
                        Severity = rule.Severity,
                        Title = rule.Title,
                        Message = rule.Message,
                        LineNumber = i + 1,
                    });
                }
            }
        }

        foreach (var rule in ConfigRules)
        {
            if (rule.Check(configText))
            {
                findings.Add(new AuditFinding
                {
                    RuleId = rule.Id,
                    Severity = rule.Severity,
                    Title = rule.Title,
                    Message = rule.Message,
                });
            }
        }

        return findings
            .OrderBy(f => f.LineNumber)
            .ThenBy(f => f.Severity)
            .ToList();
    }
}

