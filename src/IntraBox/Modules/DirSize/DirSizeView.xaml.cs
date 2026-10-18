using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly ObservableCollection<SizeNode> _roots = new ObservableCollection<SizeNode>();
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DirSizeHelper.Entry> _level = new List<DirSizeHelper.Entry>();
        private readonly List<DirSizeHelper.DriveRow> _drives = new List<DirSizeHelper.DriveRow>();
        private CancellationTokenSource _expandCts;

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
            FilterBar.FilterChanged += delegate { ShowLevel(); };
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (HistoryManager.TryLoad("dirsize", out state))
                DirBox.Text = HistoryManager.GetString(state, "dir");
            if (string.IsNullOrWhiteSpace(DirBox.Text))
                DirBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            FormHint.Apply(MsgText, "双击盘符以该盘根为分析目录。展开文件夹查看下一层，双击文件定位。");
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
            if (_expandCts != null)
            {
                _expandCts.Cancel();
                _expandCts = null;
            }
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
                    FormHint.Apply(MsgText, "双击盘符以该盘根为分析目录。展开文件夹查看下一层，双击文件定位。");
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
                _level.Clear();
                ShowLevel();
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
                    _level.Clear();
                    ShowLevel();
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
            _level.Clear();
            if (result.Children != null) _level.AddRange(result.Children);
            ShowLevel();
            FormHint.Apply(MsgText, DirSizeHelper.Summarize(result));
        }

        private void ShowLevel()
        {
            _roots.Clear();
            string kw = FilterBar != null ? (FilterBar.FilterText ?? "").Trim() : "";
            for (int i = 0; i < _level.Count; i++)
            {
                DirSizeHelper.Entry entry = _level[i];
                if (kw.Length > 0 && !EntryMatches(entry, kw)) continue;
                _roots.Add(MakeNode(entry));
            }
            RebuildRows();
        }

        private void RebuildRows()
        {
            var rows = new List<SizeRow>();
            AppendRows(_roots, 0, rows);
            ResultGrid.ItemsSource = rows;
        }

        private void AppendRows(IList<SizeNode> nodes, int depth, List<SizeRow> rows)
        {
            if (nodes == null) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                SizeNode node = nodes[i];
                if (node == null || node.IsPlaceholder) continue;
                bool dir = node.Entry != null && node.Entry.IsDirectory;
                string key = node.Entry != null ? (node.Entry.FullPath ?? "") : "";
                bool expanded = dir && node.Loaded && (key.Length == 0 || !_collapsed.Contains(key));
                rows.Add(SizeRow.From(node, depth, dir, expanded));
                if (expanded) AppendRows(node.Children, depth + 1, rows);
            }
        }

        private static bool EntryMatches(DirSizeHelper.Entry entry, string kw)
        {
            if (entry == null) return false;
            return Has(entry.Name, kw) || Has(entry.FullPath, kw) || Has(entry.KindText, kw)
                || Has(entry.SizeText, kw) || Has(entry.FileCountText, kw);
        }

        private static bool Has(string text, string kw)
        {
            return text != null && text.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static SizeNode MakeNode(DirSizeHelper.Entry entry)
        {
            var node = new SizeNode();
            node.Entry = entry;
            node.Children = new ObservableCollection<SizeNode>();
            return node;
        }

        private async void Expand_Click(object sender, MouseButtonEventArgs e)
        {
            var host = sender as FrameworkElement;
            var row = host != null ? host.DataContext as SizeRow : null;
            var node = row != null ? row.Node : null;
            if (node == null || node.IsPlaceholder || node.Entry == null || !node.Entry.IsDirectory)
                return;
            e.Handled = true;
            string key = node.Entry.FullPath ?? "";
            if (node.Loaded)
            {
                if (key.Length > 0 && !_collapsed.Add(key)) _collapsed.Remove(key);
                RebuildRows();
                return;
            }
            if (node.Loading) return;
            node.Loading = true;
            string path = node.Entry.FullPath;
            string folderName = node.Entry.Name ?? path;
            if (_expandCts != null) _expandCts.Cancel();
            var cts = new CancellationTokenSource();
            _expandCts = cts;
            var token = cts.Token;
            node.SetWait("正在汇总 " + folderName + "…");
            RebuildRows();
            FormHint.Apply(MsgText, "正在汇总 " + folderName + "…");
            DirSizeHelper.ScanResult result = null;
            try
            {
                result = await Task.Run(() => DirSizeHelper.ScanLevel(path, token), token);
            }
            catch (OperationCanceledException)
            {
                node.Loading = false;
                node.Loaded = false;
                node.SetWait(null);
                RebuildRows();
                return;
            }
            catch (Exception ex)
            {
                node.Loading = false;
                node.Loaded = false;
                node.SetWait(null);
                RebuildRows();
                FormHint.Apply(MsgText, "汇总失败：" + ex.RootMessage());
                return;
            }
            finally
            {
                if (ReferenceEquals(_expandCts, cts))
                    _expandCts = null;
            }
            if (token.IsCancellationRequested)
            {
                node.Loading = false;
                node.Loaded = false;
                node.SetWait(null);
                RebuildRows();
                return;
            }
            node.Loading = false;
            node.Loaded = true;
            node.SetWait(null);
            if (key.Length > 0) _collapsed.Remove(key);
            node.Children.Clear();
            if (result.Children != null)
            {
                for (int i = 0; i < result.Children.Count; i++)
                    node.Children.Add(MakeNode(result.Children[i]));
            }
            RebuildRows();
            FormHint.Apply(MsgText, DirSizeHelper.Summarize(result));
        }

        private void ResultGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var node = SelectedNode();
            if (node == null || node.IsPlaceholder || node.Entry == null || node.Entry.IsDirectory) return;
            OpenPath(node.Entry.FullPath, true);
            e.Handled = true;
        }

        private SizeNode SelectedNode()
        {
            var row = ResultGrid.SelectedItem as SizeRow;
            return row != null ? row.Node : null;
        }

        private void Locate_Click(object sender, RoutedEventArgs e)
        {
            var node = SelectedNode();
            if (node != null && !node.IsPlaceholder && node.Entry != null && !string.IsNullOrEmpty(node.Entry.FullPath))
            {
                OpenPath(node.Entry.FullPath, true);
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
            var node = SelectedNode();
            string path = node != null && !node.IsPlaceholder && node.Entry != null
                ? node.Entry.FullPath
                : (DirBox.Text ?? "").Trim();
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

        public sealed class SizeNode : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            private string _wait;

            public DirSizeHelper.Entry Entry { get; set; }
            public bool IsPlaceholder { get; set; }
            public bool Loaded { get; set; }
            public bool Loading { get; set; }
            public ObservableCollection<SizeNode> Children { get; set; }

            public void SetWait(string text)
            {
                _wait = text;
                var h = PropertyChanged;
                if (h != null) h(this, new System.ComponentModel.PropertyChangedEventArgs("Name"));
            }

            public string Name
            {
                get
                {
                    if (!string.IsNullOrEmpty(_wait)) return _wait;
                    return IsPlaceholder ? "正在读取…" : (Entry != null ? Entry.Name : "");
                }
            }

            public string KindText { get { return Entry != null ? Entry.KindText : ""; } }
            public string SizeText { get { return Entry != null ? Entry.SizeText : ""; } }
            public string PercentText { get { return Entry != null ? Entry.PercentText : ""; } }
            public string FileCountText { get { return Entry != null ? Entry.FileCountText : ""; } }

            public static SizeNode Placeholder()
            {
                return new SizeNode
                {
                    IsPlaceholder = true,
                    Children = new ObservableCollection<SizeNode>()
                };
            }
        }

        private sealed class SizeRow
        {
            public SizeNode Node { get; set; }
            public string Name { get; set; }
            public string KindText { get; set; }
            public string SizeText { get; set; }
            public string PercentText { get; set; }
            public string FileCountText { get; set; }
            public Thickness Indent { get; set; }
            public Visibility ExpanderVisibility { get; set; }
            public string ExpanderGlyph { get; set; }

            public static SizeRow From(SizeNode node, int depth, bool directory, bool expanded)
            {
                return new SizeRow
                {
                    Node = node,
                    Name = node != null ? node.Name : "",
                    KindText = node != null ? node.KindText : "",
                    SizeText = node != null ? node.SizeText : "",
                    PercentText = node != null ? node.PercentText : "",
                    FileCountText = node != null ? node.FileCountText : "",
                    Indent = new Thickness(depth * 16, 0, 0, 0),
                    ExpanderVisibility = directory ? Visibility.Visible : Visibility.Collapsed,
                    ExpanderGlyph = expanded ? "−" : "+"
                };
            }
        }
    }
}
