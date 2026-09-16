using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>本机信息复制：活动网卡上的 IPv4 / IPv6 地址拼接。不含环回。</summary>
    public static class MachineInfoHelper
    {
        public static bool IsActiveCopyNic(NetworkInterface nic)
        {
            if (nic == null) return false;
            if (nic.OperationalStatus != OperationalStatus.Up) return false;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) return false;
            return true;
        }

        public static bool ShouldCopyFamily(AddressFamily family, bool ipv4, bool ipv6)
        {
            if (family == AddressFamily.InterNetwork) return ipv4;
            if (family == AddressFamily.InterNetworkV6) return ipv6;
            return false;
        }

        /// <summary>一行一个地址；空项跳过。无地址返回空串。</summary>
        public static string JoinLines(IList<string> lines)
        {
            if (lines == null || lines.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(lines[i]);
            }
            return sb.ToString();
        }

        public static string CollectUnicastIps(bool ipv4, bool ipv6)
        {
            var lines = new List<string>();
            NetworkInterface[] nics;
            try
            {
                nics = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch
            {
                return "";
            }
            if (nics == null) return "";
            for (int i = 0; i < nics.Length; i++)
            {
                var nic = nics[i];
                if (!IsActiveCopyNic(nic)) continue;
                UnicastIPAddressInformationCollection addrs;
                try
                {
                    addrs = nic.GetIPProperties().UnicastAddresses;
                }
                catch
                {
                    continue;
                }
                if (addrs == null) continue;
                foreach (UnicastIPAddressInformation ua in addrs)
                {
                    if (ua == null || ua.Address == null) continue;
                    if (!ShouldCopyFamily(ua.Address.AddressFamily, ipv4, ipv6)) continue;
                    string s = ua.Address.ToString();
                    if (!string.IsNullOrEmpty(s)) lines.Add(s);
                }
            }
            return JoinLines(lines);
        }
    }
}
