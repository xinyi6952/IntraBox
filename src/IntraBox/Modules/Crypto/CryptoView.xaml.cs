using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.Crypto
{
    /// <summary>
    /// 加解密：AES/DES 对称、RSA 非对称（加密/解密/签名/验签）与密钥生成。
    /// 密文与签名使用 Base64；AES/DES 密文前缀随机 IV。加解密纯逻辑已抽到 Core.CryptoHelper。
    /// </summary>
    public partial class CryptoView : UserControl, IModuleView
    {
        public CryptoView()
        {
            InitializeComponent();
            OutputBox.MaximizeToggle += (s, e) =>
            {
                bool on = !OutputBox.IsMaximized;
                InPlaceMaximize.Apply(on, OutputBox, 6, 7, ToolbarPanel, KeyRow, SignRow,
                    InputCaption, InputBox, OutputCaption);
                OutputBox.IsMaximized = on;
            };
            UpdateAlgoUi();
        }

        private int AlgoIndex => AlgoCombo == null ? 0 : AlgoCombo.SelectedIndex;

        private void AlgoCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SignBtn != null) UpdateAlgoUi();
        }

        private void UpdateAlgoUi()
        {
            bool rsa = AlgoIndex == 2;
            SignBtn.Visibility = rsa ? Visibility.Visible : Visibility.Collapsed;
            VerifyBtn.Visibility = rsa ? Visibility.Visible : Visibility.Collapsed;
            SignRow.Visibility = rsa ? Visibility.Visible : Visibility.Collapsed;
            KeyCaption.Text = rsa ? "密钥 XML" : "密钥";
        }

        private void Encrypt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var plain = Encoding.UTF8.GetBytes(InputBox.Text ?? "");
                if (plain.Length == 0) { SetMsg("输入不能为空", true); return; }
                byte[] cipher;
                if (AlgoIndex == 2)
                    cipher = CryptoHelper.RsaEncrypt(LoadRsa(false), plain);
                else
                    cipher = CryptoHelper.SymmetricEncrypt(AlgoIndex == 1, KeyBytes(), plain);
                OutputBox.Text = Convert.ToBase64String(cipher);
                SetMsg("已加密（Base64）", false);
            }
            catch (Exception ex)
            {
                SetMsg("加密失败：" + Friendly(ex), true);
            }
        }

        private void Decrypt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cipher = CryptoHelper.FromBase64(InputBox.Text);
                byte[] plain;
                if (AlgoIndex == 2)
                    plain = CryptoHelper.RsaDecrypt(LoadRsa(true), cipher);
                else
                    plain = CryptoHelper.SymmetricDecrypt(AlgoIndex == 1, KeyBytes(), cipher);
                OutputBox.Text = Encoding.UTF8.GetString(plain);
                SetMsg("已解密", false);
            }
            catch (Exception ex)
            {
                SetMsg("解密失败：" + Friendly(ex), true);
            }
        }

        private void Sign_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var data = Encoding.UTF8.GetBytes(InputBox.Text ?? "");
                if (data.Length == 0) { SetMsg("输入不能为空", true); return; }
                using (var rsa = LoadRsa(true))
                using (var sha = SHA256.Create())
                {
                    var sig = rsa.SignData(data, sha);
                    OutputBox.Text = Convert.ToBase64String(sig);
                    SignBox.Text = OutputBox.Text;
                    SetMsg("已签名（SHA256，Base64）", false);
                }
            }
            catch (Exception ex)
            {
                SetMsg("签名失败：" + Friendly(ex), true);
            }
        }

        private void Verify_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var data = Encoding.UTF8.GetBytes(InputBox.Text ?? "");
                var sigText = string.IsNullOrWhiteSpace(SignBox.Text) ? OutputBox.Text : SignBox.Text;
                var sig = CryptoHelper.FromBase64(sigText);
                using (var rsa = LoadRsa(false))
                using (var sha = SHA256.Create())
                {
                    bool ok = rsa.VerifyData(data, sha, sig);
                    SetMsg(ok ? "验签通过" : "验签失败：签名不匹配", !ok);
                }
            }
            catch (Exception ex)
            {
                SetMsg("验签失败：" + Friendly(ex), true);
            }
        }

        private void GenKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AlgoIndex == 2)
                {
                    using (var rsa = new RSACryptoServiceProvider(2048))
                    {
                        rsa.PersistKeyInCsp = false;
                        KeyBox.Text = rsa.ToXmlString(true);
                        OutputBox.Text = "公钥 XML（可公开）：\r\n" + rsa.ToXmlString(false);
                    }
                    SetMsg("已生成 RSA-2048 密钥对（私钥在「密钥 XML」）", false);
                }
                else if (AlgoIndex == 1)
                {
                    var key = new byte[8];
                    using (var rng = RandomNumberGenerator.Create())
                        rng.GetBytes(key);
                    KeyBox.Text = Convert.ToBase64String(key);
                    SetMsg("已生成 DES 密钥（Base64）", false);
                }
                else
                {
                    var key = new byte[32];
                    using (var rng = RandomNumberGenerator.Create())
                        rng.GetBytes(key);
                    KeyBox.Text = Convert.ToBase64String(key);
                    SetMsg("已生成 AES-256 密钥（Base64）", false);
                }
            }
            catch (Exception ex)
            {
                SetMsg("生成失败：" + ex.Message, true);
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(OutputBox.Text)) return;
            string err;
            if (ClipboardHelper.TrySetText(OutputBox.Text, out err))
                SetMsg("已复制", false);
            else
                SetMsg(err, true);
        }

        private RSACryptoServiceProvider LoadRsa(bool needPrivate)
        {
            return CryptoHelper.LoadRsa(KeyBox.Text, needPrivate);
        }

        private byte[] KeyBytes()
        {
            return CryptoHelper.DeriveKey(KeyBox.Text, AlgoIndex == 1);
        }

        private static string Friendly(Exception ex)
        {
            if (ex is CryptographicException) return "密钥或数据不正确（" + ex.Message + "）";
            return ex.Message;
        }

        private void SetMsg(string text, bool error)
        {
            MsgText.Foreground = FindResource(error ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = text;
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("crypto", out state)) return;
            SetComboIndex(AlgoCombo, HistoryManager.GetInt(state, "algo", 0));
            KeyBox.Text = HistoryManager.GetString(state, "key");
            InputBox.Text = HistoryManager.GetString(state, "input");
            SignBox.Text = HistoryManager.GetString(state, "sign");
            UpdateAlgoUi();
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("crypto", new Dictionary<string, object>
            {
                { "algo", AlgoCombo.SelectedIndex },
                { "key", KeyBox.Text ?? "" },
                { "input", InputBox.Text ?? "" },
                { "sign", SignBox.Text ?? "" }
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
