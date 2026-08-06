using System.Text;

namespace NetOps.Core.NetTools.Playbooks;

public sealed record PlaybookStep(string Title, string Command, string Expected, string Reasoning);

public sealed record Playbook(string Name, string Description, IReadOnlyList<PlaybookStep> Steps);

/// <summary>
/// Deterministic troubleshooting and deployment playbooks keyed by scenario.
/// Every step carries a concrete Cisco IOS-style command, the expected result,
/// and the reasoning behind it.
/// </summary>
public static class PlaybookGenerator
{
    public static IReadOnlyList<string> KnownScenarios { get; } = new[]
    {
        "new-switch",
        "bgp-flap",
        "high-cpu",
        "interface-down",
        "ospf-adjacency",
        "security-hardening",
    };

    public static Playbook Generate(string scenario)
    {
        var steps = scenario switch
        {
            "new-switch" => NewSwitchSteps(),
            "bgp-flap" => BgpFlapSteps(),
            "high-cpu" => HighCpuSteps(),
            "interface-down" => InterfaceDownSteps(),
            "ospf-adjacency" => OspfAdjacencySteps(),
            "security-hardening" => SecurityHardeningSteps(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), $"Unknown scenario '{scenario}'"),
        };

        return new Playbook(scenario, Describe(scenario), steps);
    }

    public static string RenderMarkdown(Playbook playbook)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {playbook.Name}");
        sb.AppendLine();
        sb.AppendLine(playbook.Description);
        sb.AppendLine();

        for (var i = 0; i < playbook.Steps.Count; i++)
        {
            var step = playbook.Steps[i];
            sb.AppendLine($"## Step {i + 1}: {step.Title}");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(step.Command);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine($"**Expected:** {step.Expected}");
            sb.AppendLine();
            sb.AppendLine($"*{step.Reasoning}*");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Describe(string scenario) => scenario switch
    {
        "new-switch" => "Day-0 deployment steps to bring a new access switch online with management access and baseline hardening.",
        "bgp-flap" => "Troubleshooting sequence for a flapping BGP session, from reachability up through the neighbor state machine.",
        "high-cpu" => "Systematic hunt for the cause of sustained high CPU on a router or switch.",
        "interface-down" => "Diagnostic path for an interface that is down, err-disabled, or not passing traffic.",
        "ospf-adjacency" => "Steps to validate and repair an OSPF adjacency that will not form or keeps dropping.",
        "security-hardening" => "Audit and remediation playbook for common switch hardening gaps.",
        _ => string.Empty,
    };

    private static IReadOnlyList<PlaybookStep> NewSwitchSteps() => new[]
    {
        new PlaybookStep("Set hostname and domain", "configure terminal\nhostname sw1\nip domain-name corp.example", "Hostname and domain applied.", "A unique hostname is required for logs and management; the domain is used for DNS and crypto keys."),
        new PlaybookStep("Configure management VLAN", "interface vlan 10\nip address 192.168.10.2 255.255.255.0\nno shutdown", "VLAN 10 interface is up/up with the management IP.", "Out-of-band or dedicated management addressing keeps the control plane reachable."),
        new PlaybookStep("Set default route", "ip route 0.0.0.0 0.0.0.0 192.168.10.1", "Static default route installed.", "Provides reachability beyond the management segment."),
        new PlaybookStep("Enable SSH", "crypto key generate rsa\nip ssh version 2\nline vty 0 4\ntransport input ssh", "SSH version 2 is configured on all vty lines.", "SSH v2 avoids cleartext management and the telnet attack surface."),
        new PlaybookStep("Log to server", "logging host 10.99.0.5\nservice timestamps log datetime msec", "Syslog and timestamps enabled.", "Central logs and precise timestamps make later troubleshooting possible."),
        new PlaybookStep("Verify", "show version\nshow interfaces status\nshow ip interface brief", "All configured interfaces show up/up.", "Confirms the device came up cleanly before handing over."),
    };

    private static IReadOnlyList<PlaybookStep> BgpFlapSteps() => new[]
    {
        new PlaybookStep("Confirm the session state", "show ip bgp summary", "Neighbor in Established state with stable uptime.", "The state machine tells you where the session is failing (Idle/Connect/Active/OpenConfirm)."),
        new PlaybookStep("Check reachability to peer", "ping 203.0.113.2 source 203.0.113.1", "100% success with low latency.", "TCP port 179 cannot establish without IP reachability."),
        new PlaybookStep("Verify TCP 179", "show tcp brief | include 179", "Established TCP session to peer:179.", "Confirms the transport layer is up even if BGP state says otherwise."),
        new PlaybookStep("Check ASN and update-source", "show run | include router bgp\nshow run | section router bgp", "Correct local AS and eBGP neighbor with correct remote-as.", "An ASN mismatch sends an open message and immediately resets the session."),
        new PlaybookStep("Review flap history", "show ip bgp neighbors 203.0.113.2", "Reset counter stable; no repeated 'BGP Notification' lines.", "Persistent resets usually indicate hold-timer expiry or an MD5/update-source problem."),
    };

    private static IReadOnlyList<PlaybookStep> HighCpuSteps() => new[]
    {
        new PlaybookStep("Confirm sustained utilization", "show processes cpu | include CPU", "CPU below 90% average, no sustained 5-minute spike.", "Establishes the baseline before chasing causes."),
        new PlaybookStep("Find the top consumer", "show processes cpu history\nshow processes cpu sorted | head", "One process (or interrupt) dominates, or distribution is flat.", "A single high process points at a specific feature; flat high CPU suggests forwarding-plane load."),
        new PlaybookStep("Check for broadcast storms", "show interfaces | include input rate", "Input rates near line rate on a single interface.", "L2 loops and broadcast storms show as asymmetric input saturation."),
        new PlaybookStep("Look for route churn", "show ip bgp summary\nshow ip route summary", "BGP session count stable; route table stable.", "Peering flaps trigger repeated route recalculation and can pin CPU."),
        new PlaybookStep("Check control-plane filters", "show ip access-lists", "An inbound rate-limiting or classification ACL is present on edge links.", "Unpoliced control-plane traffic is the most common cause of CPU exhaustion."),
    };

    private static IReadOnlyList<PlaybookStep> InterfaceDownSteps() => new[]
    {
        new PlaybookStep("Confirm interface state", "show interfaces status", "Interface is connected, not err-disabled.", "Err-disabled vs. administratively down vs. physical down changes the whole path."),
        new PlaybookStep("Check for err-disable", "show errdisable recovery\nshow interfaces status err-disabled", "No interface in err-disabled state; no recent 'errdisable' log entries.", "Port-security, BPDU-guard, or loop-guard violations shut interfaces down automatically."),
        new PlaybookStep("Verify admin state", "show running-config interface GigabitEthernet0/1", "No 'shutdown' under the interface.", "An administratively down port never passes traffic regardless of cable state."),
        new PlaybookStep("Check physical layer", "show interfaces GigabitEthernet0/1", "line protocol up/up; no excessive CRC or input errors.", "CRC errors and low signal point at cabling, SFP, or duplex mismatch."),
        new PlaybookStep("Recover if needed", "clear errdisable interface GigabitEthernet0/1", "Interface returns to up/up.", "Clears a transient err-disable so the port can come back without a reload."),
    };

    private static IReadOnlyList<PlaybookStep> OspfAdjacencySteps() => new[]
    {
        new PlaybookStep("Check neighbor table", "show ip ospf neighbor", "Neighbor in FULL state.", "Stuck in INIT/2WAY means hello mismatch; EXSTART/EXCHANGE means MTU or database issues."),
        new PlaybookStep("Verify area and network statements", "show run | section router ospf", "Both routers agree on area and network statements.", "Mismatched areas prevent adjacency; overlapping networks cause route conflicts."),
        new PlaybookStep("Check hello/dead timers", "show ip ospf interface", "Hello/dead timers match the neighbor.", "Timer mismatches cause the neighbor to be repeatedly declared dead."),
        new PlaybookStep("Check MTU", "show interfaces | include MTU", "MTU matches on both sides of the link.", "A smaller MTU on one side stalls database exchange during EXSTART."),
        new PlaybookStep("Check for passive interfaces", "show ip ospf interface brief", "The link appears as a non-passive OSPF interface.", "A passive interface will not send or accept hellos."),
    };

    private static IReadOnlyList<PlaybookStep> SecurityHardeningSteps() => new[]
    {
        new PlaybookStep("Encrypt secrets", "service password-encryption\nenable secret <strong-hash>", "Enable secret set; no plaintext 'enable password' in config.", "Type-9 or type-5 hashes protect credentials at rest in configs and backups."),
        new PlaybookStep("Disable telnet", "line vty 0 4\ntransport input ssh", "Only SSH transport on vty lines.", "Telnet transmits credentials in cleartext."),
        new PlaybookStep("Change SNMP community", "snmp-server community <random-string> RO", "No default 'public' community anywhere.", "Default communities let attackers walk the entire MIB."),
        new PlaybookStep("Limit management access", "ip access-list extended MGMT\npermit ip 10.0.0.0/8 any\nline vty 0 4\naccess-class MGMT in", "Only management ranges can reach vty/SSH.", "Restricting sources shrinks the management attack surface."),
        new PlaybookStep("Harden the control plane", "control-plane\nservice-policy input COPP", "Control-plane policing policy applied on edge routers.", "CAPP/CoPP drops spoofed control traffic before it consumes CPU."),
    };
}
