using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace IntraBox.Modules.BatchRename
{
    public partial class BatchRenameView : UserControl, IModuleView
    {
        private readonly List<string> _files = new List<string>();
        private readonly List<RenameRow> _rows = new List<RenameRow>();
        private readonly List<UndoItem> _undo = new List<UndoItem>();
        private CancellationTokenSource _workCts;
        private bool _busy;

        public BatchRenameView()
        {
            InitializeComponent();
            PreviewGrid.ItemsSource = _rows;
            FilterBar.Attach(PreviewGrid);
        }

        public void OnActivated() { }

        public void OnDeactivated()
        {
            if (_workCts != null) _workCts.Cancel();
        }

        private void CancelWork()
        {
            if (_workCts != null) _workCts.Cancel();
        }

        private async void PickFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var dlg = new OpenFileDialog { Title = "选择要重命名的文件", Multiselect = true, Filter = "所有文件|*.*" };
            if (dlg.ShowDialog() != true) return;
            _files.Clear();
            _files.AddRange(dlg.FileNames);
            PathText.Text = "已选 " + _files.Count + " 个文件";
            await BuildPreviewAsync();
        }

        private async void PickDir_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择目录";
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                string selected = dlg.SelectedPath;
                var opt = SubDirCheck.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                BeginWork();
                LoadingOverlay.Show(this, "正在枚举文件…", CancelWork);
                string[] found = null;
                string err = null;
                bool cancelled = false;
                try
                {
                    found = await Task.Run(() =>
                    {
                        var list = new List<string>();
                        foreach (var f in Directory.EnumerateFiles(selected, "*", opt))
                        {
                            _workCts.Token.ThrowIfCancellationRequested();
                            list.Add(f);
                        }
                        return list.ToArray();
                    });
                }
                catch (OperationCanceledException) { cancelled = true; }
                catch (Exception ex) { err = ex.RootMessage(); }
                finally { EndWork(); }
                if (!IsLoaded) return;
                if (cancelled) { SetMsg("已取消", false); return; }
                if (err != null) { SetMsg(err, true); return; }
                _files.Clear();
                _files.AddRange(found);
                PathText.Text = selected + "（" + _files.Count + " 个文件）";
                await BuildPreviewAsync();
            }
        }

        private async void Preview_Click(object sender, RoutedEventArgs e)
        {
            await BuildPreviewAsync();
        }

        private async Task BuildPreviewAsync()
        {
            if (_busy) return;
            int seq;
            if (!int.TryParse((SeqBox.Text ?? "").Trim(), out seq)) seq = 0;
            string find = FindBox.Text ?? "";
            string repl = ReplaceBox.Text ?? "";
            string prefix = PrefixBox.Text ?? "";
            string suffix = SuffixBox.Text ?? "";
            bool useRegex = RegexCheck.IsChecked == true;
            if (useRegex && !string.IsNullOrEmpty(find))
            {
                try { new Regex(find); }
                catch (Exception ex)
                {
                    SetMsg("正则错误：" + ex.RootMessage(), true);
                    return;
                }
            }
            var files = _files.ToArray();
            BeginWork();
            int total = files.Length;
            LoadingOverlay.Show(this, "正在生成预览 0/" + total, CancelWork);
            List<RenameRow> built = null;
            string err = null;
            bool cancelled = false;
            try
            {
                built = await Task.Run(() => BuildRows(files, seq, find, repl, prefix, suffix, useRegex, _workCts.Token));
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex) { err = ex.RootMessage(); }
            finally { EndWork(); }
            if (!IsLoaded) return;
            if (err == null && !cancelled && built != null)
            {
                _rows.Clear();
                _rows.AddRange(built);
            }
            FilterBar.Apply();
            if (cancelled) SetMsg("已取消。预览 " + _rows.Count + " 项", false);
            else if (err != null) SetMsg(err, true);
            else SetMsg("预览 " + _rows.Count + " 项", false);
        }

        private List<RenameRow> BuildRows(string[] files, int seq, string find, string repl, string prefix, string suffix, bool useRegex, CancellationToken token)
        {
            Regex rx = null;
            if (useRegex && !string.IsNullOrEmpty(find)) rx = new Regex(find);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<RenameRow>();
            int n = seq;
            int total = files.Length;
            for (int i = 0; i < files.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                string path = files[i];
                string dir = Path.GetDirectoryName(path) ?? "";
                string name = Path.GetFileNameWithoutExtension(path);
                string ext = Path.GetExtension(path);
                string body = name;
                if (rx != null) body = rx.Replace(body, repl ?? "");
                else if (!string.IsNullOrEmpty(find)) body = body.Replace(find, repl ?? "");
                body = prefix + body;
                if (seq != 0)
                {
                    body += n.ToString("000");
                    n++;
                }
                body += suffix;
                string neu = body + ext;
                string dest = Path.Combine(dir, neu);
                string st = "就绪";
                if (string.Equals(Path.GetFileName(path), neu, StringComparison.Ordinal)) st = "无变化";
                else if (File.Exists(dest) && !string.Equals(path, dest, StringComparison.OrdinalIgnoreCase)) st = "目标已存在";
                else if (used.Contains(dest)) st = "预览重名";
                used.Add(dest);
                rows.Add(new RenameRow { OldPath = path, OldName = Path.GetFileName(path), NewName = neu, NewPath = dest, Status = st });
                if (i % 20 == 0 || i + 1 == total)
                {
                    int done = i + 1;
                    Dispatcher.BeginInvoke(new Action(() =>
                        LoadingOverlay.SetText(this, "正在生成预览 " + done + "/" + total)));
                }
            }
            return rows;
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (_rows.Count == 0) await BuildPreviewAsync();
            if (_busy || _rows.Count == 0) return;
            if (MessageBox.Show("将按预览重命名，冲突项会跳过。继续？", "确认重命名",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            BeginWork();
            _undo.Clear();
            int total = _rows.Count;
            LoadingOverlay.Show(this, "正在重命名 0/" + total, CancelWork);
            int ok = 0, skip = 0;
            bool cancelled = false;
            var token = _workCts.Token;
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < _rows.Count; i++)
                    {
                        if (token.IsCancellationRequested) { cancelled = true; break; }
                        var row = _rows[i];
                        string status;
                        if (row.Status != "就绪")
                        {
                            skip++;
                            status = row.Status;
                        }
                        else
                        {
                            try
                            {
                                File.Move(row.OldPath, row.NewPath);
                                lock (_undo) _undo.Add(new UndoItem { From = row.NewPath, To = row.OldPath });
                                status = "已改名";
                                ok++;
                            }
                            catch (Exception ex)
                            {
                                status = "跳过：" + ex.RootMessage();
                                skip++;
                            }
                        }
                        int done = i + 1;
                        string st = status;
                        Dispatcher.Invoke(new Action(() =>
                        {
                            row.Status = st;
                            if (PreviewGrid != null) PreviewGrid.Items.Refresh();
                            LoadingOverlay.SetText(this, "正在重命名 " + done + "/" + total);
                        }));
                    }
                });
            }
            finally { EndWork(); }
            if (!IsLoaded) return;
            FilterBar.Apply();
            SetMsg((cancelled ? "已取消。" : "完成：") + "成功 " + ok + "，跳过 " + skip, skip > 0);
        }

        private async void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (_undo.Count == 0)
            {
                SetMsg("没有可撤销的操作", true);
                return;
            }
            var items = _undo.ToArray();
            BeginWork();
            int total = items.Length;
            LoadingOverlay.Show(this, "正在撤销 0/" + total, CancelWork);
            int ok = 0;
            bool cancelled = false;
            int stopAt = -1;
            var failed = new List<UndoItem>();
            var reasons = new List<string>();
            var token = _workCts.Token;
            try
            {
                await Task.Run(() =>
                {
                    for (int i = items.Length - 1; i >= 0; i--)
                    {
                        if (token.IsCancellationRequested) { cancelled = true; stopAt = i; break; }
                        try
                        {
                            if (File.Exists(items[i].From)) File.Move(items[i].From, items[i].To);
                            ok++;
                        }
                        catch (Exception ex)
                        {
                            failed.Add(items[i]);
                            if (reasons.Count < 3)
                                reasons.Add(Path.GetFileName(items[i].From) + "：" + ex.Message);
                        }
                        int done = items.Length - i;
                        Dispatcher.BeginInvoke(new Action(() =>
                            LoadingOverlay.SetText(this, "正在撤销 " + done + "/" + total)));
                    }
                });
            }
            finally { EndWork(); }
            if (!IsLoaded) return;
            _undo.Clear();
            if (cancelled && stopAt >= 0)
            {
                for (int i = 0; i <= stopAt; i++)
                    _undo.Add(items[i]);
            }
            for (int i = 0; i < failed.Count; i++)
                _undo.Add(failed[i]);
            string msg = (cancelled ? "已取消。" : "") + "已撤销 " + ok + " 项";
            if (failed.Count > 0)
                msg += "，失败 " + failed.Count + " 项：" + string.Join("；", reasons.ToArray());
            SetMsg(msg, failed.Count > 0);
            await BuildPreviewAsync();
        }

        private void BeginWork()
        {
            if (_workCts != null) _workCts.Cancel();
            _workCts = new CancellationTokenSource();
            _busy = true;
        }

        private void EndWork()
        {
            LoadingOverlay.Hide(this);
            _busy = false;
            if (_workCts != null)
            {
                _workCts.Dispose();
                _workCts = null;
            }
        }

        private void SetMsg(string t, bool err)
        {
             FormHint.Apply(MsgText, t, err);
        }

        private sealed class RenameRow
        {
            public string OldPath { get; set; }
            public string NewPath { get; set; }
            public string OldName { get; set; }
            public string NewName { get; set; }
            public string Status { get; set; }
        }

        private sealed class UndoItem
        {
            public string From { get; set; }
            public string To { get; set; }
        }
    }
}
