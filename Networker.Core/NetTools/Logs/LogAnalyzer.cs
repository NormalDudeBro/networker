using System.Text.RegularExpressions;

namespace Networker.Core.NetTools.Logs;

public enum LogSeverity
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4,
}

public sealed record LogEntry(
    DateTimeOffset? Timestamp,
    string Facility,
    string Severity,
    string Host,
    string Tag,
    string Message,
    string Raw);

public sealed record LogFinding(string RuleId, LogSeverity Severity, string Description, int LineNumber);

public sealed record LogAnalysis(
    IReadOnlyList<LogEntry> Entries,
    IReadOnlyList<LogFinding> Findings);

/// <summary>
/// Parses RFC 3164 and RFC 5424 syslog lines and flags common network
/// anomalies (BGP flaps, interface downs, auth failures, CRC errors, ...).
/// </summary>
public static class LogAnalyzer
{
    private sealed record Rule(string RuleId, LogSeverity Severity, string Description, Regex Pattern);

    private static readonly Rule[] Rules =
    {
        new("AUTH_FAILURE", LogSeverity.Critical, "Authentication failure", new Regex(@"auth(entication)?\s+(fail|error)|%SEC_LOGIN.*FAILED|login failed", RegexOptions.IgnoreCase)),
        new("DUPLICATE_IP", LogSeverity.Critical, "Duplicate IP address detected", new Regex(@"duplicate\s+(address|ip)|conflicting\s+address", RegexOptions.IgnoreCase)),
        new("BGP_FLAP", LogSeverity.Critical, "BGP session went down", new Regex(@"%BGP-|bgp\s+.*\b(down|reset)\b|adjacency.*down", RegexOptions.IgnoreCase)),
        new("INTF_DOWN", LogSeverity.Error, "Interface or line protocol is down", new Regex(@"line protocol.*(down|shutdown)|interface.*\bdown\b|is\s+(administratively\s+)?down", RegexOptions.IgnoreCase)),
        new("OSPF_NEIGHBOR", LogSeverity.Error, "OSPF adjacency lost", new Regex(@"ospf.*\b(down|flap)\b|adjacency.*(gone|lost|changing)", RegexOptions.IgnoreCase)),
        new("RELOAD", LogSeverity.Error, "Device reload or restart", new Regex(@"%SYS-5-RELOAD|system\s+(reload|restart)|has been reloaded", RegexOptions.IgnoreCase)),
        new("SPAN_TREE", LogSeverity.Warning, "Spanning tree topology change", new Regex(@"%SPANTREE|topology\s+change", RegexOptions.IgnoreCase)),
        new("HIGH_CPU", LogSeverity.Warning, "High CPU utilization", new Regex(@"cpu.*(9[0-9]|100)%|high cpu|process.*cpu", RegexOptions.IgnoreCase)),
        new("CRC_ERROR", LogSeverity.Warning, "CRC or input/output errors", new Regex(@"crc|input\s+errors?|output\s+errors?", RegexOptions.IgnoreCase)),
        new("MEMORY", LogSeverity.Warning, "Low memory", new Regex(@"low\s+on\s+memory|memory.*(low|exhausted|shortage)", RegexOptions.IgnoreCase)),
    };

    public static LogEntry? ParseSyslogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var raw = line.TrimEnd('\r', '\n');

        // RFC 5424: <PRI>1 TIMESTAMP HOST APP PROCID MSGID STRUCTURED MSG
        var rfc5424 = Regex.Match(raw, @"^<(\d{1,3})>1\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(-|[^ ]+)\s+(.*)$", RegexOptions.None);
        if (rfc5424.Success)
        {
            var pri = int.Parse(rfc5424.Groups[1].Value);
            return new LogEntry(
                TryParseTimestamp(rfc5424.Groups[2].Value),
                DecodeFacility(pri),
                DecodeSeverity(pri).ToString(),
                rfc5424.Groups[3].Value,
                rfc5424.Groups[4].Value,
                rfc5424.Groups[8].Value,
                raw);
        }

        // RFC 3164: <PRI>MMM dd HH:mm:ss HOST TAG: MESSAGE
        var rfc3164 = Regex.Match(raw, @"^<(\d{1,3})>([A-Z][a-z]{2}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})\s+(\S+)\s+([^:\s]+):?\s*(.*)$", RegexOptions.None);
        if (rfc3164.Success)
        {
            var pri = int.Parse(rfc3164.Groups[1].Value);
            return new LogEntry(
                TryParseTimestamp(rfc3164.Groups[2].Value),
                DecodeFacility(pri),
                DecodeSeverity(pri).ToString(),
                rfc3164.Groups[3].Value,
                rfc3164.Groups[4].Value,
                rfc3164.Groups[5].Value,
                raw);
        }

        // Bare message (no PRI): treat the whole line as the message.
        var bare = Regex.Match(raw, @"^(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2})?\s*(.+)$");
        return new LogEntry(
            bare.Groups[1].Success ? TryParseTimestamp(bare.Groups[1].Value) : null,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            bare.Groups[2].Value,
            raw);
    }

    public static LogAnalysis Analyze(IEnumerable<string> lines)
    {
        var entries = new List<LogEntry>();
        var findings = new List<LogFinding>();

        var lineNumber = 0;
        foreach (var line in lines)
        {
            lineNumber++;
            var entry = ParseSyslogLine(line);
            if (entry is null)
            {
                continue;
            }

            entries.Add(entry);
            foreach (var rule in Rules)
            {
                if (rule.Pattern.IsMatch(entry.Tag) || rule.Pattern.IsMatch(entry.Message))
                {
                    findings.Add(new LogFinding(rule.RuleId, rule.Severity, rule.Description, lineNumber));
                    break;
                }
            }
        }

        return new LogAnalysis(entries, findings);
    }

    public static LogSeverity Classify(string message)
    {
        foreach (var rule in Rules)
        {
            if (rule.Pattern.IsMatch(message))
            {
                return rule.Severity;
            }
        }

        return LogSeverity.Info;
    }

    private static DateTimeOffset? TryParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string DecodeFacility(int pri) => ((pri & 0xF8) >> 3).ToString();

    private static int DecodeSeverityCode(int pri) => pri & 0x07;

    private static string DecodeSeverity(int pri) => SeverityNames[DecodeSeverityCode(pri)];

    private static readonly string[] SeverityNames =
    {
        "emerg", "alert", "crit", "err", "warning", "notice", "info", "debug",
    };
}

