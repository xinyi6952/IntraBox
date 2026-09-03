using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }


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

        private async void Encrypt_Click(object sender, RoutedEventArgs e)
        {
            string plainText = InputBox.Text ?? "";
            if (string.IsNullOrEmpty(plainText)) { SetMsg("输入不能为空", true); return; }
            int algo = AlgoIndex;
            string keyText = KeyBox.Text ?? "";

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "处理中…");

            string output;
            try
            {
                output = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var plain = Encoding.UTF8.GetBytes(plainText);
                    byte[] cipher;
                    if (algo == 2)
                        cipher = CryptoHelper.RsaEncrypt(CryptoHelper.LoadRsa(keyText, false), plain);
                    else
                        cipher = CryptoHelper.SymmetricEncrypt(algo == 1, CryptoHelper.DeriveKey(keyText, algo == 1), plain);
                    return Convert.ToBase64String(cipher);
                }, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("加密失败：" + Friendly(ex), true); return; }

            if (version != _gate.Version) return;
            OutputBox.Text = output;
            SetMsg("已加密（Base64）", false);
        }

        private async void Decrypt_Click(object sender, RoutedEventArgs e)
        {
            string inputText = InputBox.Text ?? "";
            if (string.IsNullOrEmpty(inputText)) { SetMsg("输入不能为空", true); return; }
            int algo = AlgoIndex;
            string keyText = KeyBox.Text ?? "";

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "处理中…");

            string output;
            try
            {
                output = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var cipher = CryptoHelper.FromBase64(inputText);
                    byte[] plain;
                    if (algo == 2)
                        plain = CryptoHelper.RsaDecrypt(CryptoHelper.LoadRsa(keyText, true), cipher);
                    else
                        plain = CryptoHelper.SymmetricDecrypt(algo == 1, CryptoHelper.DeriveKey(keyText, algo == 1), cipher);
                    return Encoding.UTF8.GetString(plain);
                }, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("解密失败：" + Friendly(ex), true); return; }

            if (version != _gate.Version) return;
            OutputBox.Text = output;
            SetMsg("已解密", false);
        }

        private async void Sign_Click(object sender, RoutedEventArgs e)
        {
            string dataText = InputBox.Text ?? "";
            if (string.IsNullOrEmpty(dataText)) { SetMsg("输入不能为空", true); return; }
            string keyText = KeyBox.Text ?? "";

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "处理中…");

            string output;
            try
            {
                output = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var data = Encoding.UTF8.GetBytes(dataText);
                    using (var rsa = CryptoHelper.LoadRsa(keyText, true))
                    using (var sha = SHA256.Create())
                    {
                        var sig = rsa.SignData(data, sha);
                        return Convert.ToBase64String(sig);
                    }
                }, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("签名失败：" + Friendly(ex), true); return; }

            if (version != _gate.Version) return;
            OutputBox.Text = output;
            SignBox.Text = output;
            SetMsg("已签名（SHA256，Base64）", false);
        }

        private async void Verify_Click(object sender, RoutedEventArgs e)
        {
            string dataText = InputBox.Text ?? "";
            string sigText = string.IsNullOrWhiteSpace(SignBox.Text) ? OutputBox.Text : SignBox.Text;
            string keyText = KeyBox.Text ?? "";

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "处理中…");

            bool ok;
            try
            {
                ok = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var data = Encoding.UTF8.GetBytes(dataText);
                    var sig = CryptoHelper.FromBase64(sigText);
                    using (var rsa = CryptoHelper.LoadRsa(keyText, false))
                    using (var sha = SHA256.Create())
                        return rsa.VerifyData(data, sha, sig);
                }, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("验签失败：" + Friendly(ex), true); return; }

            if (version != _gate.Version) return;
            SetMsg(ok ? "验签通过" : "验签失败：签名不匹配", !ok);
        }

        private async void GenKey_Click(object sender, RoutedEventArgs e)
        {
            int algo = AlgoIndex;

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, algo == 2 ? "生成 RSA 密钥中…" : "生成密钥中…");

            try
            {
                var r = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (algo == 2)
                    {
                        using (var rsa = new RSACryptoServiceProvider(2048))
                        {
                            rsa.PersistKeyInCsp = false;
                            return new { Key = rsa.ToXmlString(true), Out = "公钥 XML（可公开）：\r\n" + rsa.ToXmlString(false), Msg = "已生成 RSA-2048 密钥对（私钥在「密钥 XML」）" };
                        }
                    }
                    else if (algo == 1)
                    {
                        var key = new byte[8];
                        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
                        return new { Key = Convert.ToBase64String(key), Out = "", Msg = "已生成 DES 密钥（Base64）" };
                    }
                    else
                    {
                        var key = new byte[32];
                        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
                        return new { Key = Convert.ToBase64String(key), Out = "", Msg = "已生成 AES-256 密钥（Base64）" };
                    }
                }, token);

                if (version != _gate.Version) return;
                KeyBox.Text = r.Key;
                if (algo == 2) OutputBox.Text = r.Out;
                SetMsg(r.Msg, false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("生成失败：" + ex.RootMessage(), true); }
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

        private static string Friendly(Exception ex)
        {
            if (ex is CryptographicException) return "密钥或数据不正确（" + ex.RootMessage() + "）";
            return ex.RootMessage();
        }

        private void SetMsg(string text, bool error)
        {
            MsgText.Foreground = FindResource(error ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = text;
            LoadingOverlay.Hide(this);
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
            CancelPending();
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
