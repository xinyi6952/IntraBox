using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.PortTest
{
    /// <summary>
    /// 端口连通性测试：TCP 连接 + 可选 Ping，显示成功/失败与耗时。
    /// </summary>
    public partial class PortTestView : UserControl, IModuleView
    {
        private readonly ObservableCollection<string> _results = new ObservableCollection<string>();
        private CancellationTokenSource _cts;
        private bool _alive = true;

        public PortTestView()
        {
            InitializeComponent();
            ResultList.ItemsSource = _results;
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            MsgText.Text = "";
            var host = HostBox.Text.Trim();
            if (string.IsNullOrEmpty(host)) { MsgText.Text = "请输入主机"; return; }
            if (!int.TryParse(PortBox.Text.Trim(), out int port) || port < 1 || port > 65535)
            {
                MsgText.Text = "请输入合法端口（1-65535）";
                return;
            }

            if (_cts != null) { _cts.Cancel(); _cts.Dispose(); }
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _results.Clear();
            AddResult("目标：" + host + ":" + port);

            if (PingCheck.IsChecked == true)
            {
                try
                {
                    using (var ping = new Ping())
                    {
                        var reply = await ping.SendPingAsync(host, 3000);
                        if (!_alive || token.IsCancellationRequested) return;
                        AddResult(reply.Status == IPStatus.Success
                            ? "Ping：成功 " + reply.RoundtripTime + " ms"
                            : "Ping：失败 " + reply.Status);
                    }
                }
                catch (Exception ex)
                {
                    if (_alive) AddResult("Ping：异常 " + ex.RootMessage());
                }
            }

            TcpClient client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            try
            {
                var sw = Stopwatch.StartNew();
                var timeout = Task.Delay(3000, token);
                var done = await Task.WhenAny(connectTask, timeout);
                sw.Stop();
                if (!_alive || token.IsCancellationRequested)
                {
                    ScheduleClose(client, connectTask);
                    return;
                }
                if (done == connectTask && !connectTask.IsFaulted)
                {
                    AddResult("TCP：" + port + " 端口连通，" + sw.ElapsedMilliseconds + " ms");
                    try { client.Close(); } catch { }
                }
                else
                {
                    AddResult("TCP：" + port + " 端口连接超时/失败");
                    ScheduleClose(client, connectTask);
                    client = null;
                }
            }
            catch (Exception ex)
            {
                if (_alive) AddResult("TCP：异常 " + ex.RootMessage());
                ScheduleClose(client, connectTask);
                client = null;
            }
        }

        private static void ScheduleClose(TcpClient client, Task connectTask)
        {
            if (client == null) return;
            connectTask.ContinueWith(t =>
            {
                try { client.Close(); } catch { }
                var ignored = t.Exception;
            }, TaskScheduler.Default);
        }

        private void AddResult(string text)
        {
            if (!_alive) return;
            _results.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text);
        }

        public void OnActivated()
        {
            _alive = true;
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("porttest", out state)) return;
            HostBox.Text = HistoryManager.GetString(state, "host");
            PortBox.Text = HistoryManager.GetString(state, "port");
            PingCheck.IsChecked = HistoryManager.GetBool(state, "ping", false);
        }

        public void OnDeactivated()
        {
            _alive = false;
            if (_cts != null)
            {
                try { _cts.Cancel(); } catch { }
                _cts.Dispose();
                _cts = null;
            }
            HistoryManager.Save("porttest", new Dictionary<string, object>
            {
                { "host", HostBox.Text ?? "" },
                { "port", PortBox.Text ?? "" },
                { "ping", PingCheck.IsChecked == true }
            });
        }
    }
}
