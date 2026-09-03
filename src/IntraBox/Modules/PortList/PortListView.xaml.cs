using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.PortList
{
    /// <summary>
    /// 端口占用查看：解析 netstat -ano，列出端口与进程映射，可结束进程。
    /// </summary>
    public partial class PortListView : UserControl, IModuleView
    {
        private readonly List<PortRow> _all = new List<PortRow>();

        public PortListView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            Refresh_Click(null, null);
        }

        public void OnDeactivated()
        {
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _all.Clear();
                _all.AddRange(ParseNetstat(RunNetstat()));
                ApplyFilter();
                SetMsg("已刷新", false);
            }
            catch (Exception ex)
            {
                SetMsg("读取失败：" + ex.RootMessage(), true);
            }
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var q = (FilterBox.Text ?? "").Trim();
            List<PortRow> view;
            if (string.IsNullOrEmpty(q))
            {
                view = new List<PortRow>(_all);
            }
            else
            {
                view = new List<PortRow>();
                foreach (var row in _all)
                {
                    if (Match(row, q)) view.Add(row);
                }
            }
            ResultGrid.ItemsSource = view;
            CountText.Text = "共 " + view.Count + " 条" + (view.Count != _all.Count ? "（筛选自 " + _all.Count + "）" : "");
        }

        private static bool Match(PortRow row, string q)
        {
            if (row.Protocol != null && row.Protocol.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (row.LocalAddress != null && row.LocalAddress.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (row.RemoteAddress != null && row.RemoteAddress.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (row.State != null && row.State.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (row.ProcessName != null && row.ProcessName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (row.Pid.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var rows = ResultGrid.SelectedItems;
            if (rows == null || rows.Count == 0)
            {
                SetMsg("请先选中行", true);
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine("协议\t本地地址\t远程地址\t状态\tPID\t进程名");
            foreach (PortRow row in rows)
            {
                sb.Append(row.Protocol).Append('\t')
                    .Append(row.LocalAddress).Append('\t')
                    .Append(row.RemoteAddress).Append('\t')
                    .Append(row.State).Append('\t')
                    .Append(row.Pid).Append('\t')
                    .Append(row.ProcessName).AppendLine();
            }
            string err;
            if (ClipboardHelper.TrySetText(sb.ToString(), out err))
                SetMsg("已复制 " + rows.Count + " 行", false);
            else
                SetMsg(err, true);
        }

        private void Kill_Click(object sender, RoutedEventArgs e)
        {
            var row = ResultGrid.SelectedItem as PortRow;
            if (row == null)
            {
                SetMsg("请先选中要结束的进程", true);
                return;
            }
            if (row.Pid <= 4)
            {
                SetMsg("不能结束系统进程 PID " + row.Pid, true);
                return;
            }

            var msg = "确定结束进程 " + row.ProcessName + " (PID " + row.Pid + ")？\n结束后占用该端口的连接会关闭。";
            if (MessageBox.Show(msg, "结束进程", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                var proc = Process.GetProcessById(row.Pid);
                proc.Kill();
                SetMsg("已结束 PID " + row.Pid, false);
                Refresh_Click(null, null);
            }
            catch (ArgumentException)
            {
                SetMsg("进程已退出", true);
                Refresh_Click(null, null);
            }
            catch (Exception ex)
            {
                SetMsg("结束失败：" + ex.RootMessage(), true);
            }
        }

        private static string RunNetstat()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Default
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) throw new InvalidOperationException("无法启动 netstat");
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                return output;
            }
        }

        private static List<PortRow> ParseNetstat(string output)
        {
            var list = new List<PortRow>();
            var cache = new Dictionary<int, string>();
            if (string.IsNullOrEmpty(output)) return list;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length < 8) continue;
                var parts = SplitWs(line);
                if (parts.Count < 4) continue;
                var proto = parts[0].ToUpperInvariant();
                if (proto != "TCP" && proto != "UDP" && proto != "TCPV6" && proto != "UDPV6")
                    continue;

                string local, remote, state;
                int pid;
                if (proto.StartsWith("UDP", StringComparison.Ordinal))
                {
                    if (parts.Count < 4) continue;
                    local = parts[1];
                    remote = parts[2];
                    state = "";
                    if (!int.TryParse(parts[parts.Count - 1], out pid)) continue;
                }
                else
                {
                    if (parts.Count < 5) continue;
                    local = parts[1];
                    remote = parts[2];
                    state = parts[3];
                    if (!int.TryParse(parts[parts.Count - 1], out pid)) continue;
                }

                list.Add(new PortRow
                {
                    Protocol = proto,
                    LocalAddress = local,
                    RemoteAddress = remote,
                    State = state,
                    Pid = pid,
                    ProcessName = ResolveName(pid, cache)
                });
            }
            return list;
        }

        private static List<string> SplitWs(string line)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            foreach (var c in line)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        parts.Add(sb.ToString());
                        sb.Clear();
                    }
                }
                else sb.Append(c);
            }
            if (sb.Length > 0) parts.Add(sb.ToString());
            return parts;
        }

        private static string ResolveName(int pid, Dictionary<int, string> cache)
        {
            string name;
            if (cache.TryGetValue(pid, out name)) return name;
            if (pid == 0)
            {
                cache[pid] = "System Idle";
                return cache[pid];
            }
            try
            {
                name = Process.GetProcessById(pid).ProcessName;
            }
            catch (ArgumentException)
            {
                name = "(已退出)";
            }
            catch
            {
                name = "(无权访问)";
            }
            cache[pid] = name;
            return name;
        }

        private void SetMsg(string text, bool error)
        {
            MsgText.Foreground = FindResource(error ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = text;
        }
    }

    public class PortRow
    {
        public string Protocol { get; set; }
        public string LocalAddress { get; set; }
        public string RemoteAddress { get; set; }
        public string State { get; set; }
        public int Pid { get; set; }
        public string ProcessName { get; set; }
    }
}
