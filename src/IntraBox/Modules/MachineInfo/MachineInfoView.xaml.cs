using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.MachineInfo
{
    /// <summary>
    /// 本机信息：主机/系统/CPU/内存/网卡 IP 与 MAC，纯本地读取。
    /// </summary>
    public partial class MachineInfoView : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        public MachineInfoView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            StartRefresh();
        }

        public void OnDeactivated()
        {
            CancelPending();
        }

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            StartRefresh();
        }

        /// <summary>先进入页面并显示遮罩，再后台采集，避免点导航时卡住。</summary>
        private async void StartRefresh()
        {
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            MsgText.Text = "正在读取本机信息…";
            try
            {
                await Dispatcher.InvokeAsync(new Action(() => { }), DispatcherPriority.Loaded);
                if (version != _gate.Version) return;
                LoadingOverlay.Show(this, "正在读取本机信息…");
                string report = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return BuildReport();
                }, token);
                if (version != _gate.Version) return;
                OutputBox.Text = report;
                MsgText.Text = "已刷新";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (version == _gate.Version)
                    MsgText.Text = "读取失败：" + ex.RootMessage();
            }
            finally
            {
                if (version == _gate.Version)
                    LoadingOverlay.Hide(this);
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(OutputBox.Text)) return;
            string err;
            if (ClipboardHelper.TrySetText(OutputBox.Text, out err))
                MsgText.Text = "已复制";
            else
                MsgText.Text = err;
        }

        private static string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【主机】");
            sb.AppendLine("主机名\t" + Environment.MachineName);
            sb.AppendLine("用户名\t" + Environment.UserName);
            sb.AppendLine("域名\t" + Environment.UserDomainName);
            sb.AppendLine();
            sb.AppendLine("【系统】");
            sb.AppendLine("系统\t" + ReadOsName());
            sb.AppendLine("产品名称\t" + ReadReg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName"));
            sb.AppendLine("显示版本\t" + ReadReg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion"));
            sb.AppendLine("内部版本\t" + ReadReg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber"));
            sb.AppendLine("Environment\t" + Environment.OSVersion);
            sb.AppendLine("系统位数\t" + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位"));
            sb.AppendLine("进程位数\t" + (Environment.Is64BitProcess ? "64 位" : "32 位"));
            sb.AppendLine(".NET\t" + Environment.Version);
            sb.AppendLine();
            sb.AppendLine("【CPU】");
            sb.AppendLine("逻辑核心\t" + Environment.ProcessorCount);
            sb.AppendLine("处理器\t" + ReadReg(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString"));
            sb.AppendLine();
            ulong total, avail;
            if (TryGetMemory(out total, out avail))
            {
                sb.AppendLine("【内存】");
                sb.AppendLine("物理总量\t" + FormatBytes((long)total));
                sb.AppendLine("物理可用\t" + FormatBytes((long)avail));
                sb.AppendLine();
            }
            sb.AppendLine("【网络】");
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                sb.AppendLine(nic.Name + " (" + nic.NetworkInterfaceType + ")");
                sb.AppendLine("  MAC\t" + FormatMac(nic.GetPhysicalAddress()));
                var props = nic.GetIPProperties();
                foreach (var ua in props.UnicastAddresses)
                {
                    sb.AppendLine("  " + ua.Address.AddressFamily + "\t" + ua.Address);
                }
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatMac(PhysicalAddress addr)
        {
            var bytes = addr.GetAddressBytes();
            if (bytes == null || bytes.Length == 0) return "(无)";
            var parts = new string[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
                parts[i] = bytes[i].ToString("X2");
            return string.Join("-", parts);
        }

        private static string ReadOsName()
        {
            int build;
            if (!int.TryParse(ReadReg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber"), out build))
            {
                if (!int.TryParse(ReadReg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild"), out build))
                    build = 0;
            }
            string product = ReadReg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
            string display = ReadReg(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            string name;
            if (build >= 22000) name = "Windows 11";
            else if (build >= 10240) name = "Windows 10";
            else if (build >= 9600) name = "Windows 8.1";
            else if (build >= 9200) name = "Windows 8";
            else if (build >= 7600) name = "Windows 7";
            else if (!string.IsNullOrEmpty(product)) name = product;
            else name = "Windows NT";

            if (!string.IsNullOrEmpty(product))
            {
                if (product.IndexOf("Pro", StringComparison.OrdinalIgnoreCase) >= 0) name += " 专业版";
                else if (product.IndexOf("Home", StringComparison.OrdinalIgnoreCase) >= 0) name += " 家庭版";
                else if (product.IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) >= 0) name += " 企业版";
                else if (product.IndexOf("Education", StringComparison.OrdinalIgnoreCase) >= 0) name += " 教育版";
            }
            if (!string.IsNullOrEmpty(display)) name += " " + display;
            if (build > 0) name += " (" + build + ")";
            return name;
        }

        private static string ReadReg(string sub, string name)
        {
            try
            {
                var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = hklm.OpenSubKey(sub))
                {
                    if (key == null) return "";
                    var v = key.GetValue(name);
                    return v == null ? "" : v.ToString().Trim();
                }
            }
            catch
            {
                return "";
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.0") + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00") + " GB";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        private static bool TryGetMemory(out ulong total, out ulong avail)
        {
            var st = new MemoryStatusEx();
            st.Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            if (GlobalMemoryStatusEx(ref st))
            {
                total = st.TotalPhys;
                avail = st.AvailPhys;
                return true;
            }
            total = 0;
            avail = 0;
            return false;
        }
    }
}
