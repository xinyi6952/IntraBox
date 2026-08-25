using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        private void EncodeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(InputBox.Text)) { SetMsg("输入不能为空", true); return; }
            var bytes = Encoding.UTF8.GetBytes(InputBox.Text);
            OutputBox.Text = Convert.ToBase64String(bytes);
            SetMsg("已编码 " + bytes.Length + " 字节");
        }

        // Base64 → 文本
        private void DecodeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputBox.Text)) { SetMsg("输入不能为空", true); return; }
            try
            {
                var bytes = Convert.FromBase64String(InputBox.Text.Trim());
                OutputBox.Text = Encoding.UTF8.GetString(bytes);
                SetMsg("已解码 " + bytes.Length + " 字节");
            }
            catch (FormatException)
            {
                OutputBox.Text = "";
                SetMsg("错误：不是合法的 Base64 字符串", true);
            }
        }

        // 任意文件 → Base64 字符串
        private void FileToBase64_Click(object sender, RoutedEventArgs e)
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

            try
            {
                var bytes = File.ReadAllBytes(dlg.FileName);
                OutputBox.Text = Convert.ToBase64String(bytes);
                SetMsg("已转换 " + fi.Name + "（" + FormatSize(fi.Length) + "）");
            }
            catch (Exception ex)
            {
                SetMsg("错误：" + ex.Message, true);
            }
        }

        // Base64 字符串 → 任意文件
        private void Base64ToFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputBox.Text)) { SetMsg("输入不能为空", true); return; }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(InputBox.Text.Trim());
            }
            catch (FormatException)
            {
                SetMsg("错误：不是合法的 Base64 字符串", true);
                return;
            }

            if (bytes.Length > SizeLimits.MaxFileBytes)
            {
                SetMsg("错误：解码后数据过大（超过 " + SizeLimits.MaxFileMb + " MB 限制）", true);
                return;
            }

            var save = new SaveFileDialog
            {
                Title = "保存文件",
                Filter = "所有文件|*.*",
                FileName = "output"
            };
            if (save.ShowDialog() != true) return;

            try
            {
                File.WriteAllBytes(save.FileName, bytes);
                SetMsg("已保存到 " + save.FileName + "（" + FormatSize(bytes.Length) + "）");
            }
            catch (Exception ex)
            {
                SetMsg("错误：" + ex.Message, true);
            }
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
        }

        private void ClearMsg()
        {
            MsgText.Text = "";
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("base64", out state)) return;
            InputBox.Text = HistoryManager.GetString(state, "input");
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("base64", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" }
            });
        }
    }
}
