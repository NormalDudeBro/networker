using System.Text;
using static Networker.Core.NetTools.Config.ConfigWriter;
using Networker.Core.Models.NetworkConfig;

namespace Networker.Core.NetTools.Config;

/// <summary>
/// Port of the Python <c>fortinet_fortigate.j2</c> Jinja2 template.
/// </summary>
internal static class FortinetFortiGateConfigTemplate
{
    public static string Render(NetworkDeviceConfig cfg)
    {
        var sb = new StringBuilder();
        W(sb, "config system global");
        W(sb, $"    set hostname \"{cfg.Hostname}\"");
        if (!string.IsNullOrEmpty(cfg.DomainName))
        {
            W(sb, $"    set hostname-fqdn \"{cfg.Hostname}.{cfg.DomainName}\"");
        }

        W(sb, "end");
        W(sb, "");

        if (cfg.DnsServers.Count > 0)
        {
            W(sb, "config system dns");
            for (int i = 0; i < cfg.DnsServers.Count; i++)
            {
                if (i == 0)
                {
                    W(sb, $"    set primary {cfg.DnsServers[i]}");
                }
                else if (i == 1)
                {
                    W(sb, $"    set secondary {cfg.DnsServers[i]}");
                }
            }

            W(sb, "end");
            W(sb, "");
        }

        if (cfg.NtpServers.Count > 0)
        {
            W(sb, "config system ntp");
            W(sb, "    set ntpsync enable");
            W(sb, "    set type custom");
            for (int i = 0; i < cfg.NtpServers.Count; i++)
            {
                W(sb, "    config ntpserver");
                W(sb, $"        edit {i + 1}");
                W(sb, $"            set server \"{cfg.NtpServers[i]}\"");
                W(sb, "        next");
                W(sb, "    end");
            }

            W(sb, "end");
            W(sb, "");
        }

        if (!string.IsNullOrEmpty(cfg.BannerMotd))
        {
            W(sb, "config system replacemsg admin \"pre_admin-disclaimer-text\"");
            W(sb, $"    set buffer \"{cfg.BannerMotd.Replace("\"", "\\\"")}\"");
            W(sb, "end");
            W(sb, "");
        }

        foreach (var iface in cfg.Interfaces)
        {
            W(sb, "config system interface");
            W(sb, $"    edit \"{ConfigTemplateFilters.FortinetInterfaceName(iface.Name)}\"");
            if (!string.IsNullOrEmpty(iface.Description))
            {
                W(sb, $"        set alias \"{iface.Description}\"");
            }

            if (iface.IpAddress is not null && iface.SubnetMask is not null)
            {
                W(sb, "        set mode static");
                W(sb, $"        set ip {iface.IpAddress} {iface.SubnetMask}");
            }

            W(sb, iface.Enabled ? "        set status up" : "        set status down");

            if (iface.Mtu != 1500)
            {
                W(sb, "        set mtu-override enable");
                W(sb, $"        set mtu {iface.Mtu}");
            }

            if (iface.VlanId is not null)
            {
                W(sb, $"        set vlanid {iface.VlanId}");
                W(sb, $"        set interface \"{ConfigTemplateFilters.FortinetParentInterface(iface.Name)}\"");
                W(sb, "        set vdom \"root\"");
            }

            if (iface.SwitchportMode == SwitchportMode.Access && iface.AccessVlan is not null)
            {
                W(sb, $"        set native-vlan {iface.AccessVlan}");
            }

            W(sb, "        set allowaccess ping");
            W(sb, "    next");
            W(sb, "end");
            W(sb, "");
        }

        foreach (var vlan in cfg.Vlans)
        {
            W(sb, "config system interface");
            W(sb, $"    edit \"vlan{vlan.VlanId}\"");
            W(sb, "        set vdom \"root\"");
            W(sb, "        set type vlan");
            W(sb, $"        set vlanid {vlan.VlanId}");
            if (!string.IsNullOrEmpty(vlan.Name))
            {
                W(sb, $"        set alias \"{vlan.Name}\"");
            }

            W(sb, "        set interface \"internal\"");
            W(sb, "    next");
            W(sb, "end");
            W(sb, "");
        }

        if (cfg.StaticRoutes.Count > 0)
        {
            W(sb, "config router static");
            for (int i = 0; i < cfg.StaticRoutes.Count; i++)
            {
                var route = cfg.StaticRoutes[i];
                W(sb, $"    edit {i + 1}");
                W(sb, $"        set dst {route.Destination} {route.Mask}");
                W(sb, $"        set gateway {route.NextHop}");
                if (route.AdminDistance != 1)
                {
                    W(sb, $"        set distance {route.AdminDistance}");
                }

                if (!string.IsNullOrEmpty(route.Name))
                {
                    W(sb, $"        set comment \"{route.Name}\"");
                }

                W(sb, "    next");
            }

            W(sb, "end");
            W(sb, "");
        }

        if (cfg.Ospf is not null)
        {
            W(sb, "config router ospf");
            W(sb, $"    set router-id {(string.IsNullOrEmpty(cfg.Ospf.RouterId) ? "0.0.0.0" : cfg.Ospf.RouterId)}");
            if (cfg.Ospf.DefaultInformationOriginate)
            {
                W(sb, "    set default-information-originate enable");
            }

            if (cfg.Ospf.ReferenceBandwidth != 100)
            {
                W(sb, $"    set auto-cost-ref-bandwidth {cfg.Ospf.ReferenceBandwidth}");
            }

            W(sb, "    config area");
            var areas = new List<int>();
            foreach (var network in cfg.Ospf.Networks)
            {
                if (areas.Contains(network.Area))
                {
                    continue;
                }

                areas.Add(network.Area);
                W(sb, $"        edit 0.0.0.{network.Area}");
                W(sb, "        next");
            }

            W(sb, "    end");

            W(sb, "    config ospf-interface");
            for (int i = 0; i < cfg.Ospf.Networks.Count; i++)
            {
                var network = cfg.Ospf.Networks[i];
                W(sb, $"        edit \"ospf-net-{i + 1}\"");
                W(sb, $"            set interface \"{ConfigTemplateFilters.FortinetOspfInterface(network.Network)}\"");
                if (cfg.Ospf.PassiveInterfaces.Contains(network.Network))
                {
                    W(sb, "            set passive enable");
                }

                W(sb, "        next");
            }

            W(sb, "    end");

            W(sb, "    config network");
            for (int i = 0; i < cfg.Ospf.Networks.Count; i++)
            {
                var network = cfg.Ospf.Networks[i];
                W(sb, $"        edit {i + 1}");
                W(sb, $"            set prefix {network.Network} {ConfigTemplateFilters.WildcardToNetmask(network.Wildcard)}");
                W(sb, $"            set area 0.0.0.{network.Area}");
                W(sb, "        next");
            }

            W(sb, "    end");
            W(sb, "end");
            W(sb, "");
        }

        if (cfg.Bgp is not null)
        {
            W(sb, "config router bgp");
            W(sb, $"    set as {cfg.Bgp.LocalAs}");
            if (!string.IsNullOrEmpty(cfg.Bgp.RouterId))
            {
                W(sb, $"    set router-id {cfg.Bgp.RouterId}");
            }

            if (cfg.Bgp.LogNeighborChanges)
            {
                W(sb, "    set log-neighbour-changes enable");
            }

            W(sb, "    config neighbor");
            foreach (var neighbor in cfg.Bgp.Neighbors)
            {
                W(sb, $"        edit \"{neighbor.IpAddress}\"");
                W(sb, $"            set remote-as {neighbor.RemoteAs}");
                if (!string.IsNullOrEmpty(neighbor.Description))
                {
                    W(sb, $"            set description \"{neighbor.Description}\"");
                }

                if (!string.IsNullOrEmpty(neighbor.Password))
                {
                    W(sb, $"            set password {neighbor.Password}");
                }

                if (!string.IsNullOrEmpty(neighbor.UpdateSource))
                {
                    W(sb, $"            set update-source \"{neighbor.UpdateSource}\"");
                }

                if (neighbor.EbgpMultihop > 0)
                {
                    W(sb, $"            set ebgp-multihop-ttl {neighbor.EbgpMultihop}");
                }

                if (!string.IsNullOrEmpty(neighbor.RouteMapIn))
                {
                    W(sb, $"            set route-map-in \"{neighbor.RouteMapIn}\"");
                }

                if (!string.IsNullOrEmpty(neighbor.RouteMapOut))
                {
                    W(sb, $"            set route-map-out \"{neighbor.RouteMapOut}\"");
                }

                W(sb, "        next");
            }

            W(sb, "    end");

            if (cfg.Bgp.Networks.Count > 0)
            {
                W(sb, "    config network");
                for (int i = 0; i < cfg.Bgp.Networks.Count; i++)
                {
                    W(sb, $"        edit {i + 1}");
                    W(sb, $"            set prefix {cfg.Bgp.Networks[i]}");
                    W(sb, "        next");
                }

                W(sb, "    end");
            }

            if (cfg.Bgp.Redistribute.Count > 0)
            {
                W(sb, "    config redistribute \"connected\"");
                if (cfg.Bgp.Redistribute.Contains("connected"))
                {
                    W(sb, "        set status enable");
                }

                W(sb, "    end");
                W(sb, "    config redistribute \"static\"");
                if (cfg.Bgp.Redistribute.Contains("static"))
                {
                    W(sb, "        set status enable");
                }

                W(sb, "    end");
                W(sb, "    config redistribute \"ospf\"");
                if (cfg.Bgp.Redistribute.Contains("ospf"))
                {
                    W(sb, "        set status enable");
                }

                W(sb, "    end");
            }

            W(sb, "end");
            W(sb, "");
        }

        if (cfg.PrefixLists.Count > 0)
        {
            W(sb, "config router prefix-list");
            foreach (var prefixList in cfg.PrefixLists)
            {
                W(sb, $"    edit \"{prefixList.Name}\"");
                W(sb, "        config rule");
                foreach (var entry in prefixList.Entries)
                {
                    W(sb, $"            edit {entry.Sequence}");
                    W(sb, $"                set action {entry.Action}");
                    W(sb, $"                set prefix {entry.Prefix}");
                    if (entry.Ge is int ge && ge != 0)
                    {
                        W(sb, $"                set ge {ge}");
                    }

                    if (entry.Le is int le && le != 0)
                    {
                        W(sb, $"                set le {le}");
                    }

                    W(sb, "            next");
                }

                W(sb, "        end");
                W(sb, "    next");
            }

            W(sb, "end");
            W(sb, "");
        }

        if (cfg.RouteMaps.Count > 0)
        {
            W(sb, "config router route-map");
            foreach (var routeMap in cfg.RouteMaps)
            {
                W(sb, $"    edit \"{routeMap.Name}\"");
                W(sb, "        config rule");
                foreach (var entry in routeMap.Entries)
                {
                    W(sb, $"            edit {entry.Sequence}");
                    W(sb, $"                set action {entry.Action}");
                    if (!string.IsNullOrEmpty(entry.MatchPrefixList))
                    {
                        W(sb, $"                set match-ip-address \"{entry.MatchPrefixList}\"");
                    }

                    if (entry.SetLocalPref is int localPref && localPref != 0)
                    {
                        W(sb, $"                set set-local-preference {localPref}");
                    }

                    if (entry.SetMed is int med && med != 0)
                    {
                        W(sb, $"                set set-metric {med}");
                    }

                    if (entry.SetWeight is int weight && weight != 0)
                    {
                        W(sb, $"                set set-weight {weight}");
                    }

                    if (!string.IsNullOrEmpty(entry.SetAsPathPrepend))
                    {
                        W(sb, $"                set set-aspath \"{entry.SetAsPathPrepend}\"");
                    }

                    if (!string.IsNullOrEmpty(entry.SetCommunity))
                    {
                        W(sb, $"                set set-community \"{entry.SetCommunity}\"");
                    }

                    W(sb, "            next");
                }

                W(sb, "        end");
                W(sb, "    next");
            }

            W(sb, "end");
            W(sb, "");
        }

        if (cfg.Acls.Count > 0)
        {
            W(sb, "config firewall address");
            foreach (var acl in cfg.Acls)
            {
                foreach (var entry in acl.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Remark) && entry.Source != "any")
                    {
                        W(sb, $"    edit \"{acl.Name}_src_{entry.Sequence}\"");
                        W(sb, "        set type ipmask");
                        W(sb, $"        set subnet {entry.Source} {ConfigTemplateFilters.WildcardToNetmask(entry.SourceWildcard)}");
                        W(sb, "    next");
                    }

                    if (string.IsNullOrEmpty(entry.Remark) && entry.Destination != "any")
                    {
                        W(sb, $"    edit \"{acl.Name}_dst_{entry.Sequence}\"");
                        W(sb, "        set type ipmask");
                        W(sb, $"        set subnet {entry.Destination} {ConfigTemplateFilters.WildcardToNetmask(entry.DestinationWildcard)}");
                        W(sb, "    next");
                    }
                }
            }

            W(sb, "end");
            W(sb, "");

            W(sb, "config firewall policy");
            int policyId = 1;
            foreach (var acl in cfg.Acls)
            {
                foreach (var entry in acl.Entries)
                {
                    if (!string.IsNullOrEmpty(entry.Remark))
                    {
                        continue;
                    }

                    W(sb, $"    edit {policyId++}");
                    W(sb, $"        set name \"{acl.Name}_{entry.Sequence}\"");
                    W(sb, "        set srcintf \"any\"");
                    W(sb, "        set dstintf \"any\"");
                    if (entry.Source == "any")
                    {
                        W(sb, "        set srcaddr \"all\"");
                    }
                    else
                    {
                        W(sb, $"        set srcaddr \"{acl.Name}_src_{entry.Sequence}\"");
                    }

                    if (entry.Destination == "any")
                    {
                        W(sb, "        set dstaddr \"all\"");
                    }
                    else
                    {
                        W(sb, $"        set dstaddr \"{acl.Name}_dst_{entry.Sequence}\"");
                    }

                    W(sb, entry.Action == AclAction.Permit ? "        set action accept" : "        set action deny");
                    W(sb, "        set schedule \"always\"");
                    switch (entry.Protocol)
                    {
                        case AclProtocol.Tcp:
                            W(sb, "        set service \"TCP\"");
                            break;
                        case AclProtocol.Udp:
                            W(sb, "        set service \"UDP\"");
                            break;
                        case AclProtocol.Icmp:
                            W(sb, "        set service \"PING\"");
                            break;
                        default:
                            W(sb, "        set service \"ALL\"");
                            break;
                    }

                    if (entry.Log)
                    {
                        W(sb, "        set logtraffic all");
                    }

                    W(sb, $"        set comments \"{(string.IsNullOrEmpty(entry.Remark) ? acl.Name : entry.Remark)}\"");
                    W(sb, "    next");
                }
            }

            sb.Append("end");
            sb.Append("\n");
            sb.Append("\n");
        }

        return sb.ToString();
    }
}
