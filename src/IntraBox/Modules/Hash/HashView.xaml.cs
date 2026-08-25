using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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

        private void HashText_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(InputBox.Text)) { SetMsg("输入不能为空"); return; }
            var bytes = Encoding.UTF8.GetBytes(InputBox.Text);
            OutputBox.Text = ComputeHex(CreateAlgorithm(), bytes);
            SetOk("已计算文本哈希");
        }

        private void HashFile_Click(object sender, RoutedEventArgs e)
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

            try
            {
                using (var algo = CreateAlgorithm())
                using (var stream = File.OpenRead(dlg.FileName))
                {
                    var hash = algo.ComputeHash(stream);
                    OutputBox.Text = ToHex(hash);
                    SetOk("已计算 " + Path.GetFileName(dlg.FileName));
                }
            }
            catch (Exception ex)
            {
                SetMsg("错误：" + ex.Message);
            }
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

        private HashAlgorithm CreateAlgorithm()
        {
            switch (AlgoCombo.SelectedIndex)
            {
                case 0: return MD5.Create();
                case 1: return SHA1.Create();
                case 3: return SHA512.Create();
                default: return SHA256.Create();
            }
        }

        private string ComputeHex(HashAlgorithm algo, byte[] bytes)
        {
            using (algo)
            {
                return ToHex(algo.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (var b in data) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private void SetOk(string text)
        {
            MsgText.Foreground = FindResource("OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = text;
        }

        private void SetMsg(string text)
        {
            MsgText.Foreground = FindResource("DangerBrush") as System.Windows.Media.Brush;
            MsgText.Text = text;
        }

        private void ClearMsg()
        {
            MsgText.Text = "";
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
