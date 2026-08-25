using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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

        public BatchRenameView()
        {
            InitializeComponent();
            PreviewGrid.ItemsSource = _rows;
        }

        public void OnActivated() { }

        public void OnDeactivated() { }

        private void PickFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "选择要重命名的文件", Multiselect = true, Filter = "所有文件|*.*" };
            if (dlg.ShowDialog() != true) return;
            _files.Clear();
            _files.AddRange(dlg.FileNames);
            PathText.Text = "已选 " + _files.Count + " 个文件";
            BuildPreview();
        }

        private void PickDir_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择目录";
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                _files.Clear();
                try
                {
                    var opt = SubDirCheck.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    _files.AddRange(Directory.GetFiles(dlg.SelectedPath, "*", opt));
                }
                catch (Exception ex)
                {
                    SetMsg(ex.Message, true);
                    return;
                }
                PathText.Text = dlg.SelectedPath + "（" + _files.Count + " 个文件）";
                BuildPreview();
            }
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            BuildPreview();
        }

        private void BuildPreview()
        {
            _rows.Clear();
            int seq;
            if (!int.TryParse((SeqBox.Text ?? "").Trim(), out seq)) seq = 0;
            string find = FindBox.Text ?? "";
            string repl = ReplaceBox.Text ?? "";
            Regex rx = null;
            if (RegexCheck.IsChecked == true && !string.IsNullOrEmpty(find))
            {
                try { rx = new Regex(find); }
                catch (Exception ex)
                {
                    SetMsg("正则错误：" + ex.Message, true);
                    PreviewGrid.Items.Refresh();
                    return;
                }
            }
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = seq;
            for (int i = 0; i < _files.Count; i++)
            {
                string path = _files[i];
                string dir = Path.GetDirectoryName(path) ?? "";
                string name = Path.GetFileNameWithoutExtension(path);
                string ext = Path.GetExtension(path);
                string body = name;
                if (rx != null) body = rx.Replace(body, repl ?? "");
                else if (!string.IsNullOrEmpty(find)) body = body.Replace(find, repl ?? "");
                body = (PrefixBox.Text ?? "") + body;
                if (seq != 0)
                {
                    body += n.ToString("000");
                    n++;
                }
                body += SuffixBox.Text ?? "";
                string neu = body + ext;
                string dest = Path.Combine(dir, neu);
                string st = "就绪";
                if (string.Equals(Path.GetFileName(path), neu, StringComparison.Ordinal)) st = "无变化";
                else if (File.Exists(dest) && !string.Equals(path, dest, StringComparison.OrdinalIgnoreCase)) st = "目标已存在";
                else if (used.Contains(dest)) st = "预览重名";
                used.Add(dest);
                _rows.Add(new RenameRow { OldPath = path, OldName = Path.GetFileName(path), NewName = neu, NewPath = dest, Status = st });
            }
            PreviewGrid.Items.Refresh();
            SetMsg("预览 " + _rows.Count + " 项", false);
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_rows.Count == 0) BuildPreview();
            if (_rows.Count == 0) return;
            if (MessageBox.Show("将按预览重命名，冲突项会跳过。继续？", "确认重命名",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _undo.Clear();
            int ok = 0, skip = 0;
            foreach (var row in _rows)
            {
                if (row.Status != "就绪") { skip++; continue; }
                try
                {
                    File.Move(row.OldPath, row.NewPath);
                    _undo.Add(new UndoItem { From = row.NewPath, To = row.OldPath });
                    row.Status = "已改名";
                    ok++;
                }
                catch (Exception ex)
                {
                    row.Status = "跳过：" + ex.Message;
                    skip++;
                }
            }
            PreviewGrid.Items.Refresh();
            SetMsg("完成：成功 " + ok + "，跳过 " + skip, skip > 0);
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undo.Count == 0)
            {
                SetMsg("没有可撤销的操作", true);
                return;
            }
            int ok = 0;
            for (int i = _undo.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (File.Exists(_undo[i].From)) File.Move(_undo[i].From, _undo[i].To);
                    ok++;
                }
                catch { }
            }
            _undo.Clear();
            SetMsg("已撤销 " + ok + " 项", false);
            BuildPreview();
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
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
