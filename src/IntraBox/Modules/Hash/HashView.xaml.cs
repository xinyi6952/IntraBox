using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.Hash
{
    /// <summary>
    /// 哈希计算：对文本或文件计算 MD5 / SHA1 / SHA256 / SHA512。
    /// 容错：空输入、文件被占用、读取失败等以非阻断红字提示。
    /// </summary>
    public partial class HashView : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }


        public HashView()
        {
            InitializeComponent();
            OutputBox.MaximizeToggle += (s, e) =>
            {
                bool on = !OutputBox.IsMaximized;
                InPlaceMaximize.Apply(on, OutputBox, 4, 5, ToolbarPanel, InputCaption, InputBox, OutputCaption);
                OutputBox.IsMaximized = on;
            };
        }

        private async void HashText_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(InputBox.Text)) { SetMsg("输入不能为空"); return; }
            string text = InputBox.Text;
            int algoIndex = AlgoCombo.SelectedIndex;

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            SetBusy("计算中…");

            try
            {
                string hex = await Task.Run(() => ComputeHexText(text, algoIndex, token), token);
                if (version != _gate.Version) return;
                OutputBox.Text = hex;
                SetOk("已计算文本哈希");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (version == _gate.Version) SetMsg("错误：" + ex.RootMessage());
            }
        }

        private async void HashFile_Click(object sender, RoutedEventArgs e)
        {
            ClearMsg();
            var dlg = new OpenFileDialog { Title = "选择要计算哈希的文件", Filter = "所有文件|*.*" };
            if (dlg.ShowDialog() != true) return;

            string sizeErr;
            if (!SizeLimits.TryCheckFile(dlg.FileName, out sizeErr))
            {
                SetMsg(sizeErr);
                return;
            }

            string path = dlg.FileName;
            int algoIndex = AlgoCombo.SelectedIndex;

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            SetBusy("计算中…");

            try
            {
                string hex = await Task.Run(() => ComputeFileHash(path, algoIndex, token), token);
                if (version != _gate.Version) return;
                OutputBox.Text = hex;
                SetOk("已计算 " + Path.GetFileName(path));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (version == _gate.Version) SetMsg("错误：" + ex.RootMessage());
            }
        }

        private static string ComputeHexText(string text, int algoIndex, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var bytes = Encoding.UTF8.GetBytes(text);
            return ComputeHex(algoIndex, bytes);
        }

        private static string ComputeFileHash(string path, int algoIndex, CancellationToken token)
        {
            using (var algo = CreateAlgorithm(algoIndex))
            using (var stream = File.OpenRead(path))
            {
                token.ThrowIfCancellationRequested();
                return ToHex(algo.ComputeHash(stream));
            }
        }

        private static string ComputeHex(int algoIndex, byte[] bytes)
        {
            using (var algo = CreateAlgorithm(algoIndex))
            {
                return ToHex(algo.ComputeHash(bytes));
            }
        }

        private static HashAlgorithm CreateAlgorithm(int index)
        {
            switch (index)
            {
                case 0: return MD5.Create();
                case 1: return SHA1.Create();
                case 3: return SHA512.Create();
                default: return SHA256.Create();
            }
        }

        private static string ToHex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (var b in data) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(OutputBox.Text))
            {
                string err;
                if (!ClipboardHelper.TrySetText(OutputBox.Text, out err))
                    SetMsg(err);
            }
        }

        private void SetOk(string text)
        {
            MsgText.Foreground = FindResource("OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = text;
            LoadingOverlay.Hide(this);
        }

        private void SetMsg(string text)
        {
            MsgText.Foreground = FindResource("DangerBrush") as System.Windows.Media.Brush;
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
            if (!HistoryManager.TryLoad("hash", out state)) return;
            SetComboIndex(AlgoCombo, HistoryManager.GetInt(state, "algo", 2));
            InputBox.Text = HistoryManager.GetString(state, "input");
        }

        public void OnDeactivated()
        {
            CancelPending();
            HistoryManager.Save("hash", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" },
                { "algo", AlgoCombo.SelectedIndex }
            });
        }

        private static void SetComboIndex(ComboBox combo, int index)
        {
            if (combo == null || combo.Items.Count == 0) return;
            if (index < 0 || index >= combo.Items.Count) return;
            combo.SelectedIndex = index;
        }
    }
}
