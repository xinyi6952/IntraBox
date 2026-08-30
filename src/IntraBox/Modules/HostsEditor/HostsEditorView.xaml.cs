using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
        using System.IO;
        using System.Security.Principal;
        using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.HostsEditor
{
    public partial class HostsEditorView : UserControl, IModuleView, ILeaveGuard
    {
        private const int UndoLimit = 20;

        private readonly ObservableCollection<HostsEntry> _rows = new ObservableCollection<HostsEntry>();
        private readonly List<UndoSnap> _undo = new List<UndoSnap>();
        private List<HostsEntry> _fileLines = new List<HostsEntry>();
        private string _path;
        private string _diskText = "";
        private string _savedSnapshot = "";
        private EncodingChoice _fileEnc = TextFileCodec.Utf8;
        private string _newLine = "\r\n";

        private sealed class UndoSnap
        {
            public string Text;
            public EncodingChoice Enc;
            public string Summary;
        }

        public HostsEditorView()
        {
            InitializeComponent();
            HostGrid.ItemsSource = _rows;
        }

        public void OnActivated()
        {
            _path = HostsFileHelper.DefaultHostsPath();
            PathHint.Text = _path + (IsAdmin() ? "（已是管理员）" : "（当前不是管理员，保存会失败；请先退出托盘再以管理员运行）");
            LoadFile();
        }

        public void OnDeactivated() { }

        public bool CanLeave()
        {
            if (!IsDirty()) return true;
            var r = ConfirmHelper.Unsaved("Hosts 有未保存的修改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return TrySave();
            return true;
        }

        private void CommitGrid()
        {
            try
            {
                HostGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                HostGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { /* 单元格未处于编辑状态时忽略 */ }
        }

        private string Snapshot()
        {
            return HostsFileHelper.Render(_fileLines, _rows, "\n");
        }

        private bool IsDirty()
        {
            CommitGrid();
            return Snapshot() != _savedSnapshot;
        }

        private void MarkClean()
        {
            _savedSnapshot = Snapshot();
        }

        private static bool IsAdmin()
        {
            try
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (IsDirty() && !ConfirmHelper.Action("有未保存的修改，重新加载将丢弃。继续？", "重新加载"))
                return;
            LoadFile();
        }

        private void LoadFile()
        {
            try
            {
                bool uncertain;
                string text = TextFileCodec.ReadAll(_path, TextFileCodec.Auto, SizeLimits.MaxFileBytes, out _fileEnc, out uncertain);
                _newLine = DetectNewLine(text);
                if (_diskText != text)
                    _undo.Clear();
                _diskText = text;
                ApplyParsed(text);
                SetMsg("已加载 " + _rows.Count + " 条映射（" + _fileEnc.Display + "）", false);
            }
            catch (Exception ex)
            {
                SetMsg("读取失败：" + ex.Message, true);
            }
            MarkClean();
            UpdateUndoUi();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                HostGrid.UnselectAll();
                HostGrid.SelectedIndex = -1;
                HostGrid.CurrentItem = null;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ApplyParsed(string text)
        {
            _rows.Clear();
            _fileLines = HostsFileHelper.Parse(text);
            for (int i = 0; i < _fileLines.Count; i++)
            {
                if (_fileLines[i].IsMapping)
                    _rows.Add(_fileLines[i]);
            }
        }

        private void EnableCheck_Click(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            var row = cb != null ? cb.DataContext as HostsEntry : null;
            if (row == null) return;
            HostGrid.SelectedItem = row;
            HostGrid.CurrentItem = row;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var row = new HostsEntry
            {
                IsMapping = true,
                Enabled = true,
                Ip = "127.0.0.1",
                Host = "example.local",
                Comment = ""
            };
            _rows.Add(row);
            _fileLines.Add(row);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            CommitGrid();
            var row = HostGrid.SelectedItem as HostsEntry;
            if (row == null) row = HostGrid.CurrentItem as HostsEntry;
            if (row == null)
            {
                SetMsg("请先点击要删除的行", true);
                return;
            }
            string detail = ((row.Ip ?? "") + "  " + (row.Host ?? "")).Trim();
            if (!ConfirmHelper.Delete(detail)) return;
            _rows.Remove(row);
            HostGrid.UnselectAll();
            HostGrid.SelectedIndex = -1;
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            var row = HostGrid.SelectedItem as HostsEntry;
            if (row == null) return;
            row.Enabled = !row.Enabled;
            HostGrid.Items.Refresh();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TrySave();
        }

        private bool TrySave()
        {
            CommitGrid();
            string text = HostsFileHelper.Render(_fileLines, _rows, _newLine);
            if (text == _diskText)
            {
                MarkClean();
                SetMsg("没有需要保存的改动。", false);
                return true;
            }
            string summary = HostsFileHelper.DescribeChanges(_diskText, text);
            try
            {
                ClearReadOnly(_path);
                TextFileCodec.WriteAll(_path, text, _fileEnc);
                PushUndo(_diskText, _fileEnc, summary);
                _diskText = text;
                MarkClean();
                UpdateUndoUi();
                SetMsg("已保存（" + _fileEnc.Display + "）。本次改动：\n" + summary, false);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                SetMsg(NoWritePermissionMessage(), true);
                return false;
            }
            catch (Exception ex)
            {
                SetMsg("保存失败：" + ex.Message, true);
                return false;
            }
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undo.Count == 0)
            {
                SetMsg("没有可撤销的保存。", true);
                return;
            }
            if (IsDirty() && !ConfirmHelper.Action(
                    "表格里还有未保存的修改。撤销会丢弃这些内容，并把 Hosts 恢复到上一份已保存版本。继续？",
                    "撤销确认"))
                return;

            var snap = _undo[_undo.Count - 1];
            string prompt = "将撤销上次保存，把 Hosts 恢复为上一版本。\n\n上次保存做过的改动：\n"
                + snap.Summary + "\n\n确定撤销？";
            if (!ConfirmHelper.Action(prompt, "撤销 Hosts"))
                return;

            try
            {
                ClearReadOnly(_path);
                TextFileCodec.WriteAll(_path, snap.Text, snap.Enc);
                _undo.RemoveAt(_undo.Count - 1);
                _fileEnc = snap.Enc;
                _newLine = DetectNewLine(snap.Text);
                _diskText = snap.Text;
                ApplyParsed(snap.Text);
                MarkClean();
                UpdateUndoUi();
                SetMsg("已撤销上次保存。被撤回的改动：\n" + snap.Summary, false);
            }
            catch (UnauthorizedAccessException)
            {
                SetMsg(NoWritePermissionMessage(), true);
            }
            catch (Exception ex)
            {
                SetMsg("撤销失败：" + ex.Message, true);
            }
        }

        private void PushUndo(string previousText, EncodingChoice enc, string summary)
        {
            _undo.Add(new UndoSnap
            {
                Text = previousText ?? "",
                Enc = enc,
                Summary = string.IsNullOrEmpty(summary) ? "映射条目有改动。" : summary
            });
            while (_undo.Count > UndoLimit)
                _undo.RemoveAt(0);
        }

        private void UpdateUndoUi()
        {
            if (UndoBtn == null) return;
            UndoBtn.IsEnabled = _undo.Count > 0;
            UndoBtn.ToolTip = _undo.Count == 0
                ? "没有可撤销的保存"
                : "撤销上次保存（还可连续撤销 " + _undo.Count + " 步）";
        }

        private static void ClearReadOnly(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.IsReadOnly)
                    info.IsReadOnly = false;
            }
            catch { }
        }

        private string NoWritePermissionMessage()
        {
            if (IsAdmin())
                return "当前已是管理员，但仍无法写入 Hosts：\n" + _path
                    + "\n请检查文件是否只读，或安全软件是否拦截。";
            return "没有写入权限。当前进程不是管理员。"
                + " IntraBox 关窗口后仍在托盘运行，请先右键托盘图标退出，再右键 IntraBox.exe 选择「以管理员身份运行」。\n路径：" + _path;
        }

        private static string DetectNewLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "\r\n";
            if (text.IndexOf("\r\n", StringComparison.Ordinal) >= 0) return "\r\n";
            if (text.IndexOf('\n') >= 0) return "\n";
            if (text.IndexOf('\r') >= 0) return "\r";
            return "\r\n";
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }
    }
}
