using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.CertDecode
{
    /// <summary>X.509 证书解码：选文件或粘贴 PEM 解析。解析与格式化纯逻辑已抽到 Core.CertDecodeHelper。</summary>
    public partial class CertDecodeView : UserControl, IModuleView
    {
        public CertDecodeView()
        {
            InitializeComponent();
        }

        public void OnActivated() { }
        public void OnDeactivated()
        {
            HistoryManager.Save("certdecode", new Dictionary<string, object> { { "input", InputBox.Text ?? "" } });
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择证书",
                Filter = "证书|*.cer;*.crt;*.pem;*.pfx;*.p12|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                string pwd = PwdBox.Password;
                X509Certificate2 cert;
                if (dlg.FileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
                    || dlg.FileName.EndsWith(".p12", StringComparison.OrdinalIgnoreCase))
                    cert = new X509Certificate2(dlg.FileName, pwd ?? "");
                else
                    cert = new X509Certificate2(dlg.FileName);
                Show(cert);
            }
            catch (Exception ex)
            {
                SetMsg("解析失败：" + ex.Message, true);
            }
        }

        private void Parse_Click(object sender, RoutedEventArgs e)
        {
            X509Certificate2 cert;
            string err;
            if (!CertDecodeHelper.TryParsePem(InputBox.Text, out cert, out err))
            {
                SetMsg(err, true);
                return;
            }
            Show(cert);
        }

        private void Show(X509Certificate2 cert)
        {
            using (cert)
            {
                OutputBox.Text = CertDecodeHelper.Format(cert);
                SetMsg("已解析", false);
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            string err;
            if (ClipboardHelper.TrySetText(OutputBox.Text ?? "", out err)) SetMsg("已复制", false);
            else SetMsg(err, true);
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }
    }
}
