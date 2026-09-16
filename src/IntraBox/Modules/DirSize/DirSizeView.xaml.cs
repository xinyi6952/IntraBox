using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;
using WinForms = System.Windows.Forms;

namespace IntraBox.Modules.DirSize
{
    /// <summary>
    /// 目录大小：盘符概览 + 按层汇总。切走取消扫描。
    /// </summary>
    public partial class DirSizeView : UserControl, IModuleView, IBusyTool
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();
        private readonly List<DirSizeHelper.Entry> _children = new List<DirSizeHelper.Entry>();
        private readonly List<DirSizeHelper.DriveRow> _drives = new List<DirSizeHelper.DriveRow>();

        public bool IsBusy
        {
            get { return LoadingOverlay.IsShowing(this); }
        }

        public string BusyMessage
        {
            get { return "目录大小仍在分析中，离开将取消当前任务。确定离开？"; }
        }

        public DirSizeView()
        {
            InitializeComponent();
            DriveGrid.ItemsSource = _drives;
            ChildGrid.ItemsSource = _children;
            FilterBar.Attach(ChildGrid);
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (HistoryManager.TryLoad("dirsize", out state))
                DirBox.Text = HistoryManager.GetString(state, "dir");
            if (string.IsNullOrWhiteSpace(DirBox.Text))
                DirBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            FormHint.Apply(MsgText, "双击盘符以该盘根为分析目录；双击子目录进入下一层。");
            LoadDrives();
        }

        public void OnDeactivated()
        {
            CancelPending();
            HistoryManager.Save("dirsize", new Dictionary<string, object>
            {
                { "dir", DirBox.Text ?? "" }
            });
        }

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }

        private async void LoadDrives()
        {
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            try
            {
                await Dispatcher.InvokeAsync(new Action(() => { }), DispatcherPriority.Loaded);
                if (version != _gate.Version) return;
                LoadingOverlay.Show(this, "正在读取磁盘…");
                var rows = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return DirSizeHelper.ListDrives();
                }, token);
                if (version != _gate.Version) return;
                _drives.Clear();
                _drives.AddRange(rows);
                DriveGrid.Items.Refresh();
                if (string.IsNullOrEmpty(MsgText.Text))
                    FormHint.Apply(MsgText, "双击盘符以该盘根为分析目录；双击子目录进入下一层。");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (version == _gate.Version)
                    FormHint.Apply(MsgText, "读取磁盘失败：" + ex.RootMessage());
            }
            finally
            {
                if (version == _gate.Version)
                {
                    LoadingOverlay.Hide(this);
                    _gate.DisposeCurrent();
                }
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择要汇总的目录";
                if (!string.IsNullOrWhiteSpace(DirBox.Text) && Directory.Exists(DirBox.Text))
                    dlg.SelectedPath = DirBox.Text;
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                    DirBox.Text = dlg.SelectedPath;
            }
        }

        private void DriveGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = DriveGrid.SelectedItem as DirSizeHelper.DriveRow;
            if (row == null) return;
            if (!row.Ready)
            {
                FormHint.Apply(MsgText, "该盘未就绪。");
                return;
            }
            DirBox.Text = row.Name;
            Scan_Click(sender, e);
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            string parent = DirSizeHelper.ParentPath(DirBox.Text);
            if (string.IsNullOrEmpty(parent))
            {
                FormHint.Apply(MsgText, "已经在最上层。");
                return;
            }
            DirBox.Text = parent;
            Scan_Click(sender, e);
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            string dir = (DirBox.Text ?? "").Trim();
            if (!Directory.Exists(dir))
            {
                _children.Clear();
                ChildGrid.Items.Refresh();
                FormHint.Apply(MsgText, "目录不存在。");
                return;
            }

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            FormHint.Apply(MsgText, "正在汇总…");
            LoadingOverlay.Show(this, "正在汇总目录大小…");

            DirSizeHelper.ScanResult result = null;
            try
            {
                result = await Task.Run(() => DirSizeHelper.ScanLevel(dir, token), token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (version == _gate.Version)
                {
                    _children.Clear();
                    ChildGrid.Items.Refresh();
                    FormHint.Apply(MsgText, "汇总失败：" + ex.RootMessage());
                }
                return;
            }
            finally
            {
                if (version == _gate.Version)
                {
                    LoadingOverlay.Hide(this);
                    _gate.DisposeCurrent();
                }
            }

            if (version != _gate.Version) return;
            DirBox.Text = result.Path;
            _children.Clear();
            _children.AddRange(result.Children);
            ChildGrid.Items.Refresh();
            FilterBar.Apply();
            FormHint.Apply(MsgText, DirSizeHelper.Summarize(result));
        }

        private void ChildGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var entry = ChildGrid.SelectedItem as DirSizeHelper.Entry;
            if (entry == null) return;
            if (entry.IsDirectory)
            {
                DirBox.Text = entry.FullPath;
                Scan_Click(sender, e);
            }
            else
            {
                OpenPath(entry.FullPath, true);
            }
        }

        private void Locate_Click(object sender, RoutedEventArgs e)
        {
            var entry = ChildGrid.SelectedItem as DirSizeHelper.Entry;
            if (entry != null && !string.IsNullOrEmpty(entry.FullPath))
            {
                OpenPath(entry.FullPath, true);
                return;
            }
            string dir = (DirBox.Text ?? "").Trim();
            if (Directory.Exists(dir))
                OpenPath(dir, false);
            else
                FormHint.Apply(MsgText, "请先选中一条结果或填写存在的目录。");
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var entry = ChildGrid.SelectedItem as DirSizeHelper.Entry;
            string path = entry != null ? entry.FullPath : (DirBox.Text ?? "").Trim();
            string error;
            if (!ClipboardHelper.TrySetText(path, out error))
                FormHint.Apply(MsgText, error);
            else
                FormHint.Apply(MsgText, "已复制路径。");
        }

        private void OpenPath(string path, bool select)
        {
            try
            {
                if (select)
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                    Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                FormHint.Apply(MsgText, "无法打开资源管理器：" + ex.RootMessage());
            }
        }
    }
}
