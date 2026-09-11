using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.WinTopmost
{
    public partial class WinTopmostView : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        public WinTopmostView()
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

        /// <summary>先进入页面并显示遮罩，再后台枚举窗口，避免点导航时卡住。</summary>
        private async void StartRefresh()
        {
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            bool includeSelf = SelfCheck != null && SelfCheck.IsChecked == true;
            int self = Process.GetCurrentProcess().Id;
            SetMsg("正在列出窗口…", false);
            try
            {
                await Dispatcher.InvokeAsync(new Action(() => { }), DispatcherPriority.Loaded);
                if (version != _gate.Version) return;
                LoadingOverlay.Show(this, "正在列出窗口…");
                List<WindowEntry> list = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var rows = Win32Native.ListVisibleWindows();
                    if (!includeSelf)
                        rows.RemoveAll(w => w.Pid == self);
                    return rows;
                }, token);
                if (version != _gate.Version) return;
                WinList.ItemsSource = list;
                SetMsg("共 " + list.Count + " 个窗口", false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (version == _gate.Version)
                    SetMsg("读取失败：" + ex.RootMessage(), true);
            }
            finally
            {
                if (version == _gate.Version)
                    LoadingOverlay.Hide(this);
            }
        }

        private void Top_Click(object sender, RoutedEventArgs e)
        {
            SetTop(true);
        }

        private void Untop_Click(object sender, RoutedEventArgs e)
        {
            SetTop(false);
        }

        private void SetTop(bool top)
        {
            var item = WinList.SelectedItem as WindowEntry;
            if (item == null)
            {
                SetMsg("请先选中窗口", true);
                return;
            }
            IntPtr insert = new IntPtr(top ? Win32Native.HwndTopmost : Win32Native.HwndNotopmost);
            bool ok = Win32Native.SetWindowPos(item.Handle, insert, 0, 0, 0, 0, Win32Native.SwpNomove | Win32Native.SwpNosize);
            SetMsg(ok ? (top ? "已置顶" : "已取消置顶") : "操作失败", !ok);
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }
    }
}
