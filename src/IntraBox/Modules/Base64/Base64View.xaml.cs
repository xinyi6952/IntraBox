using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.Base64
{
    /// <summary>
    /// Base64 编解码：文本与文件（含图片，不限类型）的 Base64 互转。
    /// 容错：空输入、非法 Base64、文件过大等均以非阻断红字提示。
    /// </summary>
    public partial class Base64View : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }

        public Base64View()
        {
            InitializeComponent();
            OutputBox.MaximizeToggle += (s, e) =>
            {
                bool on = !OutputBox.IsMaximized;
                InPlaceMaximize.Apply(on, OutputBox, 4, 5, ToolbarPanel, InputCaption, InputBox, OutputCaption);
                OutputBox.IsMaximized = on;
            };
        }

        // 文本 → Base64
        private async void EncodeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(InputBox.Text)) { SetMsg("输入不能为空", true); return; }
            string text = InputBox.Text;
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            SetBusy("编码中…");
            try
            {
                var result = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var bytes = Encoding.UTF8.GetBytes(text);
                    return new { Text = Convert.ToBase64String(bytes), Bytes = bytes.Length };
                }, token);
                if (version != _gate.Version) return;
                OutputBox.Text = result.Text;
                SetMsg("已编码 " + result.Bytes + " 字节");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("错误：" + ex.RootMessage(), true); }
        }

        // Base64 → 文本
        private async void DecodeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputBox.Text)) { SetMsg("输入不能为空", true); return; }
            string input = InputBox.Text.Trim();
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            SetBusy("解码中…");
            try
            {
                var result = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var bytes = Convert.FromBase64String(input);
                    return new { Text = Encoding.UTF8.GetString(bytes), Bytes = bytes.Length };
                }, token);
                if (version != _gate.Version) return;
                OutputBox.Text = result.Text;
                SetMsg("已解码 " + result.Bytes + " 字节");
            }
            catch (OperationCanceledException) { }
            catch (FormatException)
            {
                if (version == _gate.Version) { OutputBox.Text = ""; SetMsg("错误：不是合法的 Base64 字符串", true); }
            }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("错误：" + ex.RootMessage(), true); }
        }

        // 任意文件 → Base64 字符串
        private async void FileToBase64_Click(object sender, RoutedEventArgs e)
        {
            ClearMsg();
            var dlg = new OpenFileDialog
            {
                Title = "选择要转换的文件",
                Filter = "所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            var fi = new FileInfo(dlg.FileName);
            string sizeErr;
            if (!SizeLimits.TryCheckFile(dlg.FileName, out sizeErr))
            {
                SetMsg(sizeErr, true);
                return;
            }

            string path = dlg.FileName;
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            SetBusy("读取并编码中…");
            try
            {
                string result = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var bytes = File.ReadAllBytes(path);
                    token.ThrowIfCancellationRequested();
                    return Convert.ToBase64String(bytes);
                }, token);
                if (version != _gate.Version) return;
                OutputBox.Text = result;
                SetMsg("已转换 " + fi.Name + "（" + FormatSize(fi.Length) + "）");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("错误：" + ex.RootMessage(), true); }
        }

        // Base64 字符串 → 任意文件
        private async void Base64ToFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputBox.Text)) { SetMsg("输入不能为空", true); return; }
            string input = InputBox.Text.Trim();

            var save = new SaveFileDialog
            {
                Title = "保存文件",
                Filter = "所有文件|*.*",
                FileName = "output"
            };
            if (save.ShowDialog() != true) return;

            string path = save.FileName;
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            SetBusy("解码并保存中…");
            try
            {
                long len = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var bytes = Convert.FromBase64String(input);
                    if (bytes.Length > SizeLimits.MaxFileBytes)
                        throw new InvalidOperationException("解码后数据过大（超过 " + SizeLimits.MaxFileMb + " MB 限制）");
                    File.WriteAllBytes(path, bytes);
                    return (long)bytes.Length;
                }, token);
                if (version != _gate.Version) return;
                SetMsg("已保存到 " + path + "（" + FormatSize(len) + "）");
            }
            catch (OperationCanceledException) { }
            catch (FormatException) { if (version == _gate.Version) SetMsg("错误：不是合法的 Base64 字符串", true); }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("错误：" + ex.RootMessage(), true); }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        }

        private void SetMsg(string text, bool isError = false)
        {
            MsgText.Foreground = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32));
            MsgText.Text = text;
            LoadingOverlay.Hide(this);
        }

        private void SetBusy(string text)
        {
            MsgText.Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
            MsgText.Text = text;
            LoadingOverlay.Show(this, text);
        }

        private void ClearMsg()
        {
            MsgText.Text = "";
            LoadingOverlay.Hide(this);
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("base64", out state)) return;
            InputBox.Text = HistoryManager.GetString(state, "input");
        }

        public void OnDeactivated()
        {
            CancelPending();
            HistoryManager.Save("base64", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" }
            });
        }
    }
}
