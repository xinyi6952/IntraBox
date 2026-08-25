using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;
using Markdig;

namespace IntraBox.Modules.MdPreview
{
    public partial class MdPreviewView : UserControl, IModuleView, ILeaveGuard
    {
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private bool _restoring;
        private bool _previewMax;
        private string _filePath;
        private EncodingChoice _fileEnc;
        private string _savedText = "";

        public MdPreviewView()
        {
            InitializeComponent();
            _timer.Interval = TimeSpan.FromMilliseconds(350);
            _timer.Tick += (s, e) => { _timer.Stop(); Render(); };
            InputBox.TextChangedByUser += (s, e) =>
            {
                if (_restoring) return;
                _timer.Stop();
                _timer.Start();
            };
        }

        private void MaxBtn_Click(object sender, RoutedEventArgs e)
        {
            _previewMax = !_previewMax;
            InPlaceMaximize.Apply(_previewMax, PreviewHost, 0, 1, 2, 3, LeftPane, Splitter);
            if (IconExpand != null) IconExpand.Visibility = _previewMax ? Visibility.Collapsed : Visibility.Visible;
            if (IconRestore != null) IconRestore.Visibility = _previewMax ? Visibility.Visible : Visibility.Collapsed;
            MaxBtn.ToolTip = _previewMax ? "还原布局" : "最大化预览";
        }

        private void OpenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsDirty() && !ConfirmHelper.Action("有未保存的修改，打开新文件将丢弃修改，确定打开？", "打开确认"))
                return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "打开 Markdown 文件",
                Filter = "Markdown|*.md;*.markdown|文本|*.txt|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                EncodingChoice used;
                bool uncertain;
                InputBox.Text = TextFileCodec.ReadAll(dlg.FileName, TextFileCodec.Auto, SizeLimits.MaxFileBytes, out used, out uncertain);
                _filePath = dlg.FileName;
                _fileEnc = used;
                _savedText = InputBox.Text ?? "";
                Render();
            }
            catch (Exception ex)
            {
                string msg = (ex.Message ?? "").Replace("<", "&lt;").Replace(">", "&gt;");
                Preview.NavigateToString("<pre>" + msg + "</pre>");
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            Save();
        }

        private void ReloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                MessageBox.Show("尚未打开文件。", "重载", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (IsDirty() && !ConfirmHelper.Action("有未保存的修改，重载将丢弃修改，确定重载？", "重载确认"))
                return;
            try
            {
                EncodingChoice used;
                InputBox.Text = TextFileCodec.ReadAll(_filePath, TextFileCodec.Auto, SizeLimits.MaxFileBytes, out used);
                _fileEnc = used;
                _savedText = InputBox.Text ?? "";
                Render();
            }
            catch (Exception ex)
            {
                MessageBox.Show("重载失败：" + ex.Message, "重载", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsDirty() && !ConfirmHelper.Action("有未保存的修改，清空将丢弃修改，确定清空？", "清空确认"))
                return;
            InputBox.Text = "";
            _filePath = null;
            _savedText = "";
            Render();
            HistoryManager.Save("mdpreview", new Dictionary<string, object> { { "input", "" }, { "file", "" } });
        }

        public void OnActivated()
        {
            Dictionary<string, object> st;
            if (HistoryManager.TryLoad("mdpreview", out st))
            {
                _restoring = true;
                InputBox.Text = HistoryManager.GetString(st, "input");
                var file = HistoryManager.GetString(st, "file");
                _filePath = string.IsNullOrEmpty(file) ? null : file;
                _restoring = false;
            }
            _savedText = InputBox.Text ?? "";
            Render();
        }

        public void OnDeactivated()
        {
            _timer.Stop();
            HistoryManager.Save("mdpreview", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" },
                { "file", _filePath ?? "" }
            });
        }

        public bool CanLeave()
        {
            if (!IsDirty()) return true;
            var r = ConfirmHelper.Unsaved("Markdown 有未保存的修改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Save();
            return true;
        }

        private bool IsDirty()
        {
            return (InputBox.Text ?? "") != _savedText;
        }

        private bool Save()
        {
            string path = _filePath;
            if (string.IsNullOrEmpty(path))
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存 Markdown",
                    Filter = "Markdown|*.md|文本|*.txt|所有文件|*.*",
                    FileName = "untitled.md"
                };
                if (dlg.ShowDialog() != true) return false;
                path = dlg.FileName;
            }
            try
            {
                var enc = _fileEnc ?? TextFileCodec.Utf8;
                TextFileCodec.WriteAll(path, InputBox.Text ?? "", enc);
                _filePath = path;
                _savedText = InputBox.Text ?? "";
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message, "保存", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void Render()
        {
            try
            {
                string html = Markdown.ToHtml(InputBox.Text ?? "");
                string page = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>"
                    + "body{font-family:Segoe UI,sans-serif;padding:12px;background:#1e1e1e;color:#ddd;}"
                    + "pre,code{background:#111;padding:4px;} a{color:#6cb6ff;}"
                    + "table{border-collapse:collapse;} td,th{border:1px solid #555;padding:4px;}"
                    + "</style></head><body>" + html + "</body></html>";
                Preview.NavigateToString(page);
            }
            catch (Exception ex)
            {
                string msg = (ex.Message ?? "").Replace("<", "&lt;").Replace(">", "&gt;");
                Preview.NavigateToString("<pre>" + msg + "</pre>");
            }
        }
    }
}
