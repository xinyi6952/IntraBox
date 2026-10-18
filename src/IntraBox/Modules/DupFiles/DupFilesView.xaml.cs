using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IntraBox.Core;
using WinForms = System.Windows.Forms;

namespace IntraBox.Modules.DupFiles
{
    public partial class DupFilesView : UserControl, IModuleView, IBusyTool
    {
        private string _dir;
        private Thread _th;
        private volatile bool _cancel;
        private volatile bool _scanning;
        private List<FilePathTree.Node> _nodes = new List<FilePathTree.Node>();
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool IsBusy
        {
            get { return _scanning; }
        }

        public string BusyMessage
        {
            get { return "重复文件查找仍在进行，离开将停止当前查找。确定离开？"; }
        }

        public DupFilesView() { InitializeComponent(); }

        public void OnActivated()
        {
            FormHint.Apply(MsgText, "先选择目录，再开始查找。查找完成会弹出结果。");
        }

        public void OnDeactivated() { _cancel = true; }

        private void Pick_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择要扫描的目录";
                if (!string.IsNullOrEmpty(_dir)) dlg.SelectedPath = _dir;
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                {
                    _dir = dlg.SelectedPath;
                    DirBox.Text = _dir;
                    FormHint.Apply(MsgText, "已选择目录，点「开始查找」。");
                }
            }
        }

        private void Scan_Click(object sender, RoutedEventArgs e)
        {
            if (_scanning)
            {
                _cancel = true;
                if (ScanBtn != null)
                {
                    ScanBtn.Content = "正在停止…";
                    ScanBtn.IsEnabled = false;
                }
                return;
            }
            if (string.IsNullOrEmpty(_dir) || !Directory.Exists(_dir))
            {
                FormHint.Apply(MsgText, "请先选择目录");
                return;
            }
            _cancel = false;
            _scanning = true;
            if (ScanBtn != null)
            {
                ScanBtn.Content = "停止";
                ScanBtn.IsEnabled = true;
            }
            Bar.IsIndeterminate = true;
            Bar.Value = 0;
            FormHint.Apply(MsgText, "正在枚举文件…");
            var dir = _dir;
            bool sub = SubCheck.IsChecked == true;
            _th = new Thread(() =>
            {
                try { Scan(dir, sub); }
                finally { Ui(EndScan); }
            });
            _th.IsBackground = true;
            _th.Start();
        }

        private void EndScan()
        {
            _scanning = false;
            if (ScanBtn != null)
            {
                ScanBtn.Content = "开始查找";
                ScanBtn.IsEnabled = true;
            }
            if (Bar != null) Bar.IsIndeterminate = false;
        }

        private void Scan(string dir, bool sub)
        {
            try
            {
                var opt = sub ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = new List<string>();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", opt))
                    {
                        if (_cancel)
                        {
                            Ui(() => FormHint.Apply(MsgText, "已停止。"));
                            return;
                        }
                        files.Add(f);
                        if (files.Count % 100 == 0)
                        {
                            int seen = files.Count;
                            Ui(() => FormHint.Apply(MsgText, "正在枚举文件… 已看到 " + seen + " 个"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Ui(() => { FormHint.Apply(MsgText, ex.RootMessage()); });
                    return;
                }
                if (_cancel)
                {
                    Ui(() => FormHint.Apply(MsgText, "已停止。已看到 " + files.Count + " 个文件。"));
                    return;
                }
                var bySize = new Dictionary<long, List<string>>();
                int unreadable = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    if (_cancel)
                    {
                        Ui(() => FormHint.Apply(MsgText, "已停止。"));
                        return;
                    }
                    try
                    {
                        long len = new FileInfo(files[i]).Length;
                        List<string> list;
                        if (!bySize.TryGetValue(len, out list)) { list = new List<string>(); bySize[len] = list; }
                        list.Add(files[i]);
                    }
                    catch { unreadable++; }
                }
                int hashTotal = 0;
                foreach (var kv in bySize)
                {
                    if (kv.Value.Count >= 2) hashTotal += kv.Value.Count;
                }
                Ui(() =>
                {
                    Bar.IsIndeterminate = false;
                    Bar.Value = 0;
                    FormHint.Apply(MsgText, "正在计算 0/" + hashTotal);
                });
                var dups = new List<DupRow>();
                int g = 1;
                int hashed = 0;
                foreach (var kv in bySize)
                {
                    if (_cancel)
                    {
                        Ui(() => FormHint.Apply(MsgText, "已停止。"));
                        return;
                    }
                    if (kv.Value.Count < 2) continue;
                    var hashes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in kv.Value)
                    {
                        if (_cancel)
                        {
                            Ui(() => FormHint.Apply(MsgText, "已停止。"));
                            return;
                        }
                        string h;
                        try { h = HashFile(f); }
                        catch { h = null; unreadable++; }
                        hashed++;
                        if (h != null)
                        {
                            List<string> list;
                            if (!hashes.TryGetValue(h, out list)) { list = new List<string>(); hashes[h] = list; }
                            list.Add(f);
                        }
                        if (hashed % 5 == 0 || hashed == hashTotal)
                        {
                            int done = hashed;
                            int total = hashTotal;
                            Ui(() =>
                            {
                                Bar.IsIndeterminate = false;
                                Bar.Value = total <= 0 ? 100 : done * 100.0 / total;
                                FormHint.Apply(MsgText, "正在计算 " + done + "/" + total);
                            });
                        }
                    }
                    foreach (var hg in hashes)
                    {
                        if (hg.Value.Count < 2) continue;
                        foreach (var p in hg.Value)
                        {
                            dups.Add(new DupRow
                            {
                                Group = g,
                                Path = p,
                                SizeText = kv.Key.ToString(),
                                HashShort = hg.Key.Length > 16 ? hg.Key.Substring(0, 16) : hg.Key
                            });
                        }
                        g++;
                    }
                }
                int groups = g - 1;
                int dupCount = dups.Count;
                int skipped = unreadable;
                Ui(() =>
                {
                    if (_cancel || !IsLoaded) return;
                    EndScan();
                    _collapsed.Clear();
                    _nodes = BuildDupTree(dir, dups);
                    BindGrid();
                    if (Bar != null)
                    {
                        Bar.IsIndeterminate = false;
                        Bar.Value = 100;
                    }
                    string done = "查找完成。目录 " + dir + "。重复组 " + groups + "，文件 " + dupCount + "。";
                    if (skipped > 0) done += "跳过 " + skipped + " 个无法读取的文件。";
                    FormHint.Apply(MsgText, done);
                    var owner = Window.GetWindow(this);
                    if (owner != null)
                        MessageBox.Show(owner, done, "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Information);
                    else
                        MessageBox.Show(done, "重复文件查找", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Ui(() => { FormHint.Apply(MsgText, "失败：" + ex.RootMessage()); });
            }
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        private void Ui(Action a)
        {
            Dispatcher.BeginInvoke(a);
        }

        private static List<FilePathTree.Node> BuildDupTree(string root, List<DupRow> dups)
        {
            var roots = new List<FilePathTree.Node>();
            int i = 0;
            while (i < dups.Count)
            {
                int group = dups[i].Group;
                var leaves = new List<FilePathTree.Leaf>();
                string size = "";
                string hash = "";
                while (i < dups.Count && dups[i].Group == group)
                {
                    DupRow row = dups[i];
                    if (size.Length == 0) size = row.SizeText ?? "";
                    if (hash.Length == 0) hash = row.HashShort ?? "";
                    leaves.Add(new FilePathTree.Leaf
                    {
                        FullPath = row.Path,
                        Detail = row.SizeText,
                        Extra = row.HashShort,
                        Payload = row
                    });
                    i++;
                }
                var header = new FilePathTree.Node();
                header.Name = "重复组 " + group;
                header.Detail = size;
                header.Extra = hash;
                header.IsFile = false;
                List<FilePathTree.Node> children = FilePathTree.Build(root, leaves);
                for (int c = 0; c < children.Count; c++)
                    header.Children.Add(children[c]);
                roots.Add(header);
            }
            return roots;
        }

        private void BindGrid()
        {
            List<FilePathTree.FlatRow> flat = FilePathTree.Flatten(_nodes, _collapsed);
            var rows = new List<DupGridRow>();
            for (int i = 0; i < flat.Count; i++)
                rows.Add(DupGridRow.From(flat[i]));
            ResultGrid.ItemsSource = rows;
        }

        private void Expand_Click(object sender, MouseButtonEventArgs e)
        {
            var host = sender as FrameworkElement;
            var row = host != null ? host.DataContext as DupGridRow : null;
            if (row == null || string.IsNullOrEmpty(row.Key) || !row.HasChildren) return;
            if (!_collapsed.Add(row.Key)) _collapsed.Remove(row.Key);
            BindGrid();
            e.Handled = true;
        }

        private DupGridRow SelectedRow()
        {
            return ResultGrid.SelectedItem as DupGridRow;
        }

        private void Locate_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow();
            var node = row != null ? row.Node : null;
            if (node == null || string.IsNullOrEmpty(node.FullPath))
            {
                FormHint.Apply(MsgText, "请先选中一个文件或目录。");
                return;
            }
            try
            {
                if (node.IsFile)
                    Process.Start("explorer.exe", "/select,\"" + node.FullPath + "\"");
                else
                    Process.Start("explorer.exe", "\"" + node.FullPath + "\"");
            }
            catch (Exception ex) { FormHint.Apply(MsgText, ex.RootMessage()); }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow();
            var node = row != null ? row.Node : null;
            var dup = node != null ? node.Payload as DupRow : null;
            if (dup == null || string.IsNullOrEmpty(dup.Path))
            {
                FormHint.Apply(MsgText, "请先选中一个文件。");
                return;
            }
            if (!ConfirmHelper.Delete(dup.Path)) return;
            try
            {
                File.Delete(dup.Path);
                FormHint.Apply(MsgText, "已删除");
            }
            catch (Exception ex) { FormHint.Apply(MsgText, "删除失败：" + ex.RootMessage()); }
        }

        private sealed class DupRow
        {
            public int Group { get; set; }
            public string Path { get; set; }
            public string SizeText { get; set; }
            public string HashShort { get; set; }
        }

        private sealed class DupGridRow
        {
            public FilePathTree.Node Node { get; set; }
            public string Key { get; set; }
            public bool HasChildren { get; set; }
            public string Name { get; set; }
            public string SizeText { get; set; }
            public string HashText { get; set; }
            public string PathText { get; set; }
            public Thickness Indent { get; set; }
            public Visibility ExpanderVisibility { get; set; }
            public string ExpanderGlyph { get; set; }

            public static DupGridRow From(FilePathTree.FlatRow flat)
            {
                FilePathTree.Node node = flat.Node;
                return new DupGridRow
                {
                    Node = node,
                    Key = flat.Key,
                    HasChildren = flat.HasChildren,
                    Name = node != null ? node.Name : "",
                    SizeText = node != null ? (node.Detail ?? "") : "",
                    HashText = node != null ? (node.Extra ?? "") : "",
                    PathText = node != null ? (node.FullPath ?? "") : "",
                    Indent = new Thickness(flat.Depth * 16, 0, 0, 0),
                    ExpanderVisibility = flat.HasChildren ? Visibility.Visible : Visibility.Collapsed,
                    ExpanderGlyph = flat.Expanded ? "−" : "+"
                };
            }
        }
    }
}
