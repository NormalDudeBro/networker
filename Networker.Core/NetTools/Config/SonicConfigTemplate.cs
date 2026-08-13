using System.Net;
using System.Text;
using static Networker.Core.NetTools.Config.ConfigWriter;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.NetTools.Config;

/// <summary>
/// Port of the Python <c>sonic.j2</c> Jinja2 template (config_db.json format).
/// </summary>
internal static class SonicConfigTemplate
{
    public static string Render(NetworkDeviceConfig cfg)
    {
        var sb = new StringBuilder();
        var portList = cfg.Interfaces
            .Where(i => i.InterfaceType is not (InterfaceType.Loopback or InterfaceType.Vlan or InterfaceType.PortChannel))
            .ToList();
        var l3Interfaces = cfg.Interfaces
            .Where(i => i.IpAddress is not null && i.SubnetMask is not null
                && i.InterfaceType is not (InterfaceType.Loopback or InterfaceType.Vlan))
            .ToList();
        var loopbackInterfaces = cfg.Interfaces
            .Where(i => i.InterfaceType == InterfaceType.Loopback && i.IpAddress is not null && i.SubnetMask is not null)
            .ToList();
        var portchannelInterfaces = cfg.Interfaces
            .Where(i => i.InterfaceType == InterfaceType.PortChannel)
            .ToList();
        var pcWithIp = portchannelInterfaces
            .Where(i => i.IpAddress is not null && i.SubnetMask is not null)
            .ToList();
        var channelMembers = cfg.Interfaces
            .Where(i => i.ChannelGroup is not null)
            .ToList();
        var vlanMembers = cfg.Interfaces
            .Where(i => i.VlanId is not null || i.AccessVlan is not null || i.IsTrunk)
            .ToList();
        var vlanInterfaces = cfg.Interfaces
            .Where(i => i.InterfaceType == InterfaceType.Vlan && i.IpAddress is not null && i.SubnetMask is not null)
            .ToList();

        var sections = new List<string> { "DEVICE_METADATA" };
        if (portList.Count > 0) sections.Add("PORT");
        if (l3Interfaces.Count > 0) sections.Add("INTERFACE");
        if (loopbackInterfaces.Count > 0) sections.Add("LOOPBACK_INTERFACE");
        if (portchannelInterfaces.Count > 0) sections.Add("PORTCHANNEL");
        if (pcWithIp.Count > 0) sections.Add("PORTCHANNEL_INTERFACE");
        if (channelMembers.Count > 0) sections.Add("PORTCHANNEL_MEMBER");
        if (cfg.Vlans.Count > 0) sections.Add("VLAN");
        if (vlanMembers.Count > 0) sections.Add("VLAN_MEMBER");
        if (vlanInterfaces.Count > 0) sections.Add("VLAN_INTERFACE");
        if (cfg.StaticRoutes.Count > 0) sections.Add("STATIC_ROUTE");
        if (cfg.Ospf is not null)
        {
            sections.Add("OSPF_ROUTER");
            sections.Add("OSPF_ROUTER_AREA");
            sections.Add("OSPF_INTERFACE");
        }

        if (cfg.Bgp is not null && cfg.Bgp.Neighbors.Count > 0) sections.Add("BGP_NEIGHBOR");
        if (cfg.Acls.Count > 0)
        {
            sections.Add("ACL_TABLE");
            sections.Add("ACL_RULE");
        }

        if (cfg.NtpServers.Count > 0) sections.Add("NTP_SERVER");
        if (cfg.DnsServers.Count > 0) sections.Add("DNS_NAMESERVER");

        bool NotLast(string section) => sections[^1] != section;

        for (int i = 0; i < 10; i++)
        {
            sb.Append('\n');
        }

        W(sb, "{");
        W(sb, "    \"DEVICE_METADATA\": {");
        W(sb, "        \"localhost\": {");
        sb.Append($"            \"hostname\": \"{cfg.Hostname}\",\n");
        if (cfg.Bgp is not null)
        {
            sb.Append($"            \"bgp_asn\": \"{cfg.Bgp.LocalAs}\",\n");
        }

        W(sb, "            \"type\": \"ToRRouter\",");
        W(sb, "            \"synchronous_mode\": \"enable\"");
        W(sb, "        }");
        sb.Append("    }");
        if (sections.Count > 1) sb.Append(',');
        sb.Append('\n');

        if (portList.Count > 0)
        {
            W(sb, "    \"PORT\": {");
            for (int i = 0; i < portList.Count; i++)
            {
                var iface = portList[i];
                W(sb, $"        \"{ConfigTemplateFilters.SonicInterfaceName(iface.Name)}\": {{");
                sb.Append($"            \"admin_status\": \"{(iface.Enabled ? "up" : "down")}\",\n");
                if (!string.IsNullOrEmpty(iface.Description))
                {
                    sb.Append($"            \"description\": \"{iface.Description}\",\n");
                }

                var mtuValue = iface.Mtu != 1500 ? iface.Mtu.ToString() : "9100";
                if (!string.IsNullOrEmpty(iface.Speed))
                {
                    sb.Append($"            \"mtu\": \"{mtuValue}\",\n");
                    sb.Append($"            \"speed\": \"{iface.Speed}\"");
                }
                else
                {
                    sb.Append($"            \"mtu\": \"{mtuValue}\"");
                }

                sb.Append("        }");
                if (i != portList.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("PORT")) sb.Append(',');
            sb.Append('\n');
        }

        if (l3Interfaces.Count > 0)
        {
            W(sb, "    \"INTERFACE\": {");
            for (int i = 0; i < l3Interfaces.Count; i++)
            {
                var iface = l3Interfaces[i];
                IPAddress ipAddress = iface.IpAddress!;
                IPAddress subnetMask = iface.SubnetMask!;
                sb.Append($"        \"{ConfigTemplateFilters.SonicInterfaceName(iface.Name)}|{ipAddress}/{ConfigTemplateFilters.SubnetToCidr(subnetMask.ToString())}\": {{}}");
                if (i != l3Interfaces.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("INTERFACE")) sb.Append(',');
            sb.Append('\n');
        }

        if (loopbackInterfaces.Count > 0)
        {
            W(sb, "    \"LOOPBACK_INTERFACE\": {");
            for (int i = 0; i < loopbackInterfaces.Count; i++)
            {
                var iface = loopbackInterfaces[i];
                IPAddress ipAddress = iface.IpAddress!;
                IPAddress subnetMask = iface.SubnetMask!;
                sb.Append($"        \"{ConfigTemplateFilters.SonicInterfaceName(iface.Name)}|{ipAddress}/{ConfigTemplateFilters.SubnetToCidr(subnetMask.ToString())}\": {{}}");
                if (i != loopbackInterfaces.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("LOOPBACK_INTERFACE")) sb.Append(',');
            sb.Append('\n');
        }

        if (portchannelInterfaces.Count > 0)
        {
            W(sb, "    \"PORTCHANNEL\": {");
            for (int i = 0; i < portchannelInterfaces.Count; i++)
            {
                var iface = portchannelInterfaces[i];
                W(sb, $"        \"{ConfigTemplateFilters.SonicInterfaceName(iface.Name)}\": {{");
                sb.Append($"            \"admin_status\": \"{(iface.Enabled ? "up" : "down")}\"");
                if (iface.Mtu != 1500)
                {
                    sb.Append($",\n            \"mtu\": \"{iface.Mtu}\"");
                }

                sb.Append("        }");
                if (i != portchannelInterfaces.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("PORTCHANNEL")) sb.Append(',');
            sb.Append('\n');
        }

        if (pcWithIp.Count > 0)
        {
            W(sb, "    \"PORTCHANNEL_INTERFACE\": {");
            for (int i = 0; i < pcWithIp.Count; i++)
            {
                var iface = pcWithIp[i];
                IPAddress ipAddress = iface.IpAddress!;
                IPAddress subnetMask = iface.SubnetMask!;
                sb.Append($"        \"{ConfigTemplateFilters.SonicInterfaceName(iface.Name)}|{ipAddress}/{ConfigTemplateFilters.SubnetToCidr(subnetMask.ToString())}\": {{}}");
                if (i != pcWithIp.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("PORTCHANNEL_INTERFACE")) sb.Append(',');
            sb.Append('\n');
        }

        if (channelMembers.Count > 0)
        {
            W(sb, "    \"PORTCHANNEL_MEMBER\": {");
            for (int i = 0; i < channelMembers.Count; i++)
            {
                var iface = channelMembers[i];
                int channelGroup = iface.ChannelGroup!.Value;
                sb.Append($"        \"PortChannel{channelGroup:D4}|{ConfigTemplateFilters.SonicInterfaceName(iface.Name)}\": {{}}");
                if (i != channelMembers.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("PORTCHANNEL_MEMBER")) sb.Append(',');
            sb.Append('\n');
        }

        if (cfg.Vlans.Count > 0)
        {
            W(sb, "    \"VLAN\": {");
            for (int i = 0; i < cfg.Vlans.Count; i++)
            {
                var vlan = cfg.Vlans[i];
                W(sb, $"        \"Vlan{vlan.VlanId}\": {{");
                sb.Append($"            \"vlanid\": \"{vlan.VlanId}\"\n");
                sb.Append("        }");
                if (i != cfg.Vlans.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("VLAN")) sb.Append(',');
            sb.Append('\n');
        }

        if (vlanMembers.Count > 0)
        {
            W(sb, "    \"VLAN_MEMBER\": {");
            for (int i = 0; i < vlanMembers.Count; i++)
            {
                var iface = vlanMembers[i];
                if (iface.VlanId is not null || iface.AccessVlan is not null)
                {
                    sb.Append($"        \"Vlan{iface.VlanId ?? iface.AccessVlan}|{ConfigTemplateFilters.SonicInterfaceName(iface.Name)}\": {{\n");
                    sb.Append("            \"tagging_mode\": \"untagged\"\n");
                    sb.Append("        }");
                    if (i != vlanMembers.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                else if (iface.IsTrunk && !string.IsNullOrEmpty(iface.TrunkAllowedVlans))
                {
                    var trunkVlans = iface.TrunkAllowedVlans.Split(',');
                    for (int j = 0; j < trunkVlans.Length; j++)
                    {
                        sb.Append($"        \"Vlan{trunkVlans[j].Trim()}|{ConfigTemplateFilters.SonicInterfaceName(iface.Name)}\": {{\n");
                        sb.Append("            \"tagging_mode\": \"tagged\"\n");
                        sb.Append("        }");
                        if (j != trunkVlans.Length - 1) sb.Append(',');
                        sb.Append('\n');
                    }
                }
            }

            sb.Append("    }");
            if (NotLast("VLAN_MEMBER")) sb.Append(',');
            sb.Append('\n');
        }

        if (vlanInterfaces.Count > 0)
        {
            W(sb, "    \"VLAN_INTERFACE\": {");
            for (int i = 0; i < vlanInterfaces.Count; i++)
            {
                var iface = vlanInterfaces[i];
                IPAddress ipAddress = iface.IpAddress!;
                IPAddress subnetMask = iface.SubnetMask!;
                sb.Append($"        \"Vlan{ConfigTemplateFilters.SonicVlanId(iface.Name)}|{ipAddress}/{ConfigTemplateFilters.SubnetToCidr(subnetMask.ToString())}\": {{}}");
                if (i != vlanInterfaces.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("VLAN_INTERFACE")) sb.Append(',');
            sb.Append('\n');
        }

        if (cfg.StaticRoutes.Count > 0)
        {
            W(sb, "    \"STATIC_ROUTE\": {");
            for (int i = 0; i < cfg.StaticRoutes.Count; i++)
            {
                var route = cfg.StaticRoutes[i];
                W(sb, $"        \"{route.Destination}/{ConfigTemplateFilters.SubnetToCidr(route.Mask)}\": {{");
                sb.Append($"            \"nexthop\": \"{route.NextHop}\"");
                if (route.AdminDistance != 1)
                {
                    sb.Append($",\n            \"distance\": \"{route.AdminDistance}\"");
                }

                sb.Append("        }");
                if (i != cfg.StaticRoutes.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("STATIC_ROUTE")) sb.Append(',');
            sb.Append('\n');
        }

        if (cfg.Ospf is not null)
        {
            W(sb, "    \"OSPF_ROUTER\": {");
            W(sb, "        \"default\": {");
            sb.Append($"            \"router_id\": \"{(string.IsNullOrEmpty(cfg.Ospf.RouterId) ? "0.0.0.0" : cfg.Ospf.RouterId)}\"");
            if (cfg.Ospf.ReferenceBandwidth != 100)
            {
                sb.Append($",\n            \"reference_bandwidth\": \"{cfg.Ospf.ReferenceBandwidth}\"");
            }

            if (cfg.Ospf.DefaultInformationOriginate)
            {
                sb.Append(",\n            \"default_information_originate\": \"true\"");
            }

            sb.Append("        }\n");
            sb.Append("    }");
            if (NotLast("OSPF_ROUTER")) sb.Append(',');
            sb.Append('\n');

            var areas = new List<int>();
            foreach (var network in cfg.Ospf.Networks)
            {
                if (!areas.Contains(network.Area))
                {
                    areas.Add(network.Area);
                }
            }

            W(sb, "    \"OSPF_ROUTER_AREA\": {");
            for (int i = 0; i < areas.Count; i++)
            {
                var areaDotted = areas[i] < 256 ? $"0.0.0.{areas[i]}" : areas[i].ToString();
                sb.Append($"        \"default|{areaDotted}\": {{}}");
                if (i != areas.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("OSPF_ROUTER_AREA")) sb.Append(',');
            sb.Append('\n');

            W(sb, "    \"OSPF_INTERFACE\": {");
            for (int i = 0; i < cfg.Ospf.Networks.Count; i++)
            {
                var network = cfg.Ospf.Networks[i];
                var areaDotted = network.Area < 256 ? $"0.0.0.{network.Area}" : network.Area.ToString();
                W(sb, $"        \"{ConfigTemplateFilters.SonicInterfaceName(network.Network)}\": {{");
                sb.Append($"            \"area\": \"{areaDotted}\"");
                if (cfg.Ospf.PassiveInterfaces.Contains(network.Network))
                {
                    sb.Append(",\n            \"passive\": \"true\"");
                }

                sb.Append("        }");
                if (i != cfg.Ospf.Networks.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("OSPF_INTERFACE")) sb.Append(',');
            sb.Append('\n');
        }

        if (cfg.Bgp is not null && cfg.Bgp.Neighbors.Count > 0)
        {
            W(sb, "    \"BGP_NEIGHBOR\": {");
            for (int i = 0; i < cfg.Bgp.Neighbors.Count; i++)
            {
                var neighbor = cfg.Bgp.Neighbors[i];
                var isIp = !string.IsNullOrEmpty(neighbor.UpdateSource)
                    && char.IsDigit(neighbor.UpdateSource[0])
                    && neighbor.UpdateSource.Contains('.');
                W(sb, $"        \"{neighbor.IpAddress}\": {{");
                sb.Append($"            \"rmt_asn\": \"{neighbor.RemoteAs}\",\n");
                if (!string.IsNullOrEmpty(neighbor.Description))
                {
                    sb.Append($"            \"name\": \"{neighbor.Description}\",\n");
                }

                if (isIp)
                {
                    sb.Append($"            \"local_addr\": \"{neighbor.UpdateSource}\",\n");
                }

                sb.Append("            \"holdtime\": \"180\",\n");
                sb.Append("            \"keepalive\": \"60\"\n");
                sb.Append("        }");
                if (i != cfg.Bgp.Neighbors.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("BGP_NEIGHBOR")) sb.Append(',');
            sb.Append('\n');
        }

        if (cfg.Acls.Count > 0)
        {
            W(sb, "    \"ACL_TABLE\": {");
            for (int i = 0; i < cfg.Acls.Count; i++)
            {
                var acl = cfg.Acls[i];
                W(sb, $"        \"{acl.Name}\": {{");
                W(sb, "            \"type\": \"L3\",");
                sb.Append($"            \"policy_desc\": \"{acl.Name}\"");
                if (portList.Count > 0)
                {
                    sb.Append(",\n");
                    W(sb, "            \"stage\": \"ingress\",");
                    W(sb, "            \"ports\": [");
                    for (int j = 0; j < portList.Count; j++)
                    {
                        sb.Append($"                \"{ConfigTemplateFilters.SonicInterfaceName(portList[j].Name)}\"");
                        if (j != portList.Count - 1) sb.Append(',');
                        sb.Append('\n');
                    }

                    sb.Append("            ]        }");
                }
                else
                {
                    sb.Append("        }");
                }

                if (i != cfg.Acls.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("ACL_TABLE")) sb.Append(',');
            sb.Append('\n');

            W(sb, "    \"ACL_RULE\": {");
            for (int i = 0; i < cfg.Acls.Count; i++)
            {
                var acl = cfg.Acls[i];
                for (int j = 0; j < acl.Entries.Count; j++)
                {
                    var entry = acl.Entries[j];
                    if (!string.IsNullOrEmpty(entry.Remark))
                    {
                        continue;
                    }

                    var fields = new List<string>();
                    fields.Add($"            \"PRIORITY\": \"{10000 - entry.Sequence}\"");
                    fields.Add($"            \"PACKET_ACTION\": \"{(entry.Action == AclAction.Permit ? "FORWARD" : "DROP")}\"");
                    if (entry.Protocol != AclProtocol.Ip)
                    {
                        var proto = entry.Protocol switch
                        {
                            AclProtocol.Tcp => "6",
                            AclProtocol.Udp => "17",
                            AclProtocol.Icmp => "1",
                            _ => string.Empty,
                        };
                        fields.Add($"            \"IP_PROTOCOL\": \"{proto}\"");
                    }

                    if (entry.Source != "any")
                    {
                        fields.Add($"            \"SRC_IP\": \"{entry.Source}/{ConfigTemplateFilters.WildcardToCidr(entry.SourceWildcard)}\"");
                    }

                    if (entry.Destination != "any")
                    {
                        fields.Add($"            \"DST_IP\": \"{entry.Destination}/{ConfigTemplateFilters.WildcardToCidr(entry.DestinationWildcard)}\"");
                    }

                    if (!string.IsNullOrEmpty(entry.SourcePort))
                    {
                        fields.Add($"            \"L4_SRC_PORT\": \"{ConfigTemplateFilters.SonicPort(entry.SourcePort)}\"");
                    }

                    if (!string.IsNullOrEmpty(entry.DestinationPort))
                    {
                        fields.Add($"            \"L4_DST_PORT\": \"{ConfigTemplateFilters.SonicPort(entry.DestinationPort)}\"");
                    }

                    W(sb, $"        \"{acl.Name}|RULE_{entry.Sequence}\": {{");
                    sb.Append(string.Join(",\n", fields));
                    sb.Append("        }");
                    if (j != acl.Entries.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }

                if (i != cfg.Acls.Count - 1)
                {
                    sb.Append(',');
                }
            }

            sb.Append("    }");
            if (NotLast("ACL_RULE")) sb.Append(',');
            sb.Append('\n');
        }

        if (cfg.NtpServers.Count > 0)
        {
            W(sb, "    \"NTP_SERVER\": {");
            for (int i = 0; i < cfg.NtpServers.Count; i++)
            {
                sb.Append($"        \"{cfg.NtpServers[i]}\": {{}}");
                if (i != cfg.NtpServers.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("    }");
            if (NotLast("NTP_SERVER")) sb.Append(',');
            sb.Append('\n');
        }

        if (cfg.DnsServers.Count > 0)
        {
            W(sb, "    \"DNS_NAMESERVER\": {");
            for (int i = 0; i < cfg.DnsServers.Count; i++)
            {
                sb.Append($"        \"{cfg.DnsServers[i]}\": {{}}");
                if (i != cfg.DnsServers.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            W(sb, "    }");
        }

        sb.Append('}');

        return sb.ToString();
    }
}
