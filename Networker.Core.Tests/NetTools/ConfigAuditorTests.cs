using Networker.Core.NetTools.Config;

namespace Networker.Core.Tests.NetTools;

public class ConfigAuditorTests
{
    [Fact]
    public void Audit_FlagsPlaintextEnablePassword()
    {
        var findings = ConfigAuditor.Audit("enable password mysecret\n");
        Assert.Contains(findings, f => f.RuleId == "ENABLE_PASSWORD_PLAINTEXT" && f.Severity == AuditSeverity.Error);
    }

    [Fact]
    public void Audit_AcceptsHashedEnableSecret()
    {
        var findings = ConfigAuditor.Audit("enable secret 9 $9$abc\nservice password-encryption\n");
        Assert.DoesNotContain(findings, f => f.RuleId == "ENABLE_PASSWORD_PLAINTEXT");
        Assert.DoesNotContain(findings, f => f.RuleId == "ENABLE_SECRET_MISSING");
    }

    [Fact]
    public void Audit_FlagsDefaultSnmpCommunity()
    {
        var findings = ConfigAuditor.Audit("snmp-server community public ro\n");
        Assert.Contains(findings, f => f.RuleId == "SNMP_PUBLIC_COMMUNITY" && f.Severity == AuditSeverity.Error);
    }

    [Fact]
    public void Audit_FlagsTelnetTransport()
    {
        var findings = ConfigAuditor.Audit("line vty 0 4\n transport input telnet\n");
        Assert.Contains(findings, f => f.RuleId == "TELNET_TRANSPORT");
    }

    [Fact]
    public void Audit_DoesNotFlagSshTransport()
    {
        var findings = ConfigAuditor.Audit("line vty 0 4\n transport input ssh\n ip ssh version 2\n");
        Assert.DoesNotContain(findings, f => f.RuleId == "TELNET_TRANSPORT");
        Assert.DoesNotContain(findings, f => f.RuleId == "VTY_NO_SSH");
    }

    [Fact]
    public void Audit_WarnsWithoutPasswordEncryption()
    {
        var findings = ConfigAuditor.Audit("hostname r1\n");
        Assert.Contains(findings, f => f.RuleId == "PASSWORD_ENCRYPTION");
    }

    [Fact]
    public void Audit_ReportsLineNumbers()
    {
        var findings = ConfigAuditor.Audit("hostname r1\nenable password x\n");
        var finding = findings.Single(f => f.RuleId == "ENABLE_PASSWORD_PLAINTEXT");
        Assert.Equal(2, finding.LineNumber);
    }

    [Fact]
    public void Audit_EmptyConfig_NoFindings()
    {
        Assert.Empty(ConfigAuditor.Audit(""));
        Assert.Empty(ConfigAuditor.Audit("   \n  "));
    }

    [Fact]
    public void Audit_WarnsWithoutNtpAndLogging()
    {
        var findings = ConfigAuditor.Audit("hostname r1\n");
        Assert.Contains(findings, f => f.RuleId == "NTP_NOT_CONFIGURED");
        Assert.Contains(findings, f => f.RuleId == "LOGGING_NOT_CONFIGURED");
    }
}

