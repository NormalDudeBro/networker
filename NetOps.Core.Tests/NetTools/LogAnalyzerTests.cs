using NetOps.Core.NetTools.Logs;

namespace NetOps.Core.Tests.NetTools;

public class LogAnalyzerTests
{
    [Fact]
    public void ParseSyslogLine_Rfc3164_ExtractsFields()
    {
        var entry = LogAnalyzer.ParseSyslogLine("<131>Feb 12 14:30:00 router01 %BGP-5-ADJCHANGE: neighbor 10.0.0.2 Down");

        Assert.NotNull(entry);
        Assert.Equal("router01", entry.Host);
        Assert.Equal("16", entry.Facility);
        Assert.Equal("err", entry.Severity);
        Assert.Equal("%BGP-5-ADJCHANGE", entry.Tag);
        Assert.Contains("neighbor 10.0.0.2 Down", entry.Message);
    }

    [Fact]
    public void ParseSyslogLine_Rfc5424_ExtractsFields()
    {
        var entry = LogAnalyzer.ParseSyslogLine("<165>1 2026-08-06T14:30:00.123Z core-sw1 JUNOS 1234 - - interface ge-0/0/0 down");

        Assert.NotNull(entry);
        Assert.Equal("core-sw1", entry.Host);
        Assert.Equal("20", entry.Facility);
        Assert.Equal("notice", entry.Severity);
        Assert.Contains("interface ge-0/0/0 down", entry.Message);
        Assert.Equal(new DateTimeOffset(2026, 8, 6, 14, 30, 0, 123, TimeSpan.Zero), entry.Timestamp);
    }

    [Fact]
    public void ParseSyslogLine_BareMessage_IsParsed()
    {
        var entry = LogAnalyzer.ParseSyslogLine("link is down on port 12");

        Assert.NotNull(entry);
        Assert.Equal("link is down on port 12", entry.Message);
    }

    [Fact]
    public void ParseSyslogLine_EmptyLine_ReturnsNull()
    {
        Assert.Null(LogAnalyzer.ParseSyslogLine(""));
        Assert.Null(LogAnalyzer.ParseSyslogLine("   "));
    }

    [Fact]
    public void Analyze_FlagsBgpFlapAsCritical()
    {
        var analysis = LogAnalyzer.Analyze(new[] { "<134>Feb 12 14:30:00 r1 %BGP-5-ADJCHANGE: neighbor 10.0.0.2 Down" });

        var finding = Assert.Single(analysis.Findings);
        Assert.Equal("BGP_FLAP", finding.RuleId);
        Assert.Equal(LogSeverity.Critical, finding.Severity);
        Assert.Equal(1, finding.LineNumber);
    }

    [Fact]
    public void Analyze_FlagsAuthFailureAsCritical()
    {
        var analysis = LogAnalyzer.Analyze(new[] { "<85>Feb 12 14:31:00 r1 sshd[123]: authentication failure; user=root" });

        var finding = Assert.Single(analysis.Findings);
        Assert.Equal("AUTH_FAILURE", finding.RuleId);
        Assert.Equal(LogSeverity.Critical, finding.Severity);
    }

    [Fact]
    public void Analyze_FlagsDuplicateIpAsCritical()
    {
        var analysis = LogAnalyzer.Analyze(new[] { "<134>Feb 12 14:32:00 r1 %IP-4-DUPADDR: Duplicate address 192.168.1.5" });

        var finding = Assert.Single(analysis.Findings);
        Assert.Equal("DUPLICATE_IP", finding.RuleId);
    }

    [Fact]
    public void Analyze_FlagsHighCpuAsWarning()
    {
        var analysis = LogAnalyzer.Analyze(new[] { "<134>Feb 12 14:33:00 r1 %SYS-5-CPU: CPU utilization 97%" });

        var finding = Assert.Single(analysis.Findings);
        Assert.Equal("HIGH_CPU", finding.RuleId);
        Assert.Equal(LogSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void Analyze_CleanLine_NoFindings()
    {
        var analysis = LogAnalyzer.Analyze(new[] { "<134>Feb 12 14:34:00 r1 %SYS-5-CONFIG_I: Configured from console" });
        Assert.Empty(analysis.Findings);
        Assert.Single(analysis.Entries);
    }

    [Fact]
    public void Analyze_ReportsLineNumbersAcrossFile()
    {
        var lines = new[]
        {
            "<13>Feb 12 14:35:00 r1 %SYS-5-CONFIG_I: ok",
            "<13>Feb 12 14:36:00 r1 %SYS-5-CONFIG_I: ok",
            "<85>Feb 12 14:37:00 r1 sshd[9]: authentication failure",
        };

        var analysis = LogAnalyzer.Analyze(lines);

        var finding = Assert.Single(analysis.Findings);
        Assert.Equal(3, finding.LineNumber);
        Assert.Equal(3, analysis.Entries.Count);
    }

    [Fact]
    public void Classify_MapsMessageToSeverity()
    {
        Assert.Equal(LogSeverity.Critical, LogAnalyzer.Classify("BGP session down"));
        Assert.Equal(LogSeverity.Warning, LogAnalyzer.Classify("crc errors increasing"));
        Assert.Equal(LogSeverity.Info, LogAnalyzer.Classify("config changed"));
    }
}
