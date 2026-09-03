using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.Generator
{
    /// <summary>
    /// UUID / ULID / 密码生成器：批量生成 UUID v4、可排序 ULID 与随机密码。
    /// </summary>
    public partial class GeneratorView : UserControl, IModuleView
    {
        private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string Lower = "abcdefghijklmnopqrstuvwxyz";
        private const string Digits = "0123456789";
        private const string Symbols = "!@#$%^&*()-_=+[]{};:,.<>?";

        public GeneratorView()
        {
            InitializeComponent();
            UuidOutput.MaximizeToggle += (s, e) => ToggleUuidMax();
            PwdOutput.MaximizeToggle += (s, e) => TogglePwdMax();
            ApplyIdKindUi();
        }

        private void IdKind_Changed(object sender, SelectionChangedEventArgs e)
        {
            ApplyIdKindUi();
        }

        private void ApplyIdKindUi()
        {
            if (IdKindCombo == null || UuidUpperCheck == null || UuidNoDashCheck == null) return;
            bool uuid = IdKindCombo.SelectedIndex != 1;
            UuidUpperCheck.Visibility = uuid ? Visibility.Visible : Visibility.Collapsed;
            UuidNoDashCheck.Visibility = uuid ? Visibility.Visible : Visibility.Collapsed;
            if (GenIdBtn != null) GenIdBtn.Content = uuid ? "生成 UUID" : "生成 ULID";
        }

        private void ToggleUuidMax()
        {
            bool on = !UuidOutput.IsMaximized;
            if (on && PwdOutput.IsMaximized) TogglePwdMax();
            InPlaceMaximize.Apply(on, UuidOutput, 2, 7, UuidTitle, UuidBar, PwdTitle, PwdBar, PwdOutput);
            UuidOutput.IsMaximized = on;
        }

        private void TogglePwdMax()
        {
            bool on = !PwdOutput.IsMaximized;
            if (on && UuidOutput.IsMaximized) ToggleUuidMax();
            InPlaceMaximize.Apply(on, PwdOutput, 6, 7, UuidTitle, UuidBar, UuidOutput, PwdTitle, PwdBar);
            PwdOutput.IsMaximized = on;
        }

        private void GenUuid_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(UuidCountBox.Text.Trim(), out int count) || count < 1 || count > 1000)
            {
                UuidOutput.Text = "数量需为 1-1000 的整数";
                return;
            }

            bool ulid = IdKindCombo != null && IdKindCombo.SelectedIndex == 1;
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (ulid) sb.AppendLine(UlidUtil.NewUlid());
                else sb.AppendLine(FormatUuid(Guid.NewGuid().ToString()));
            }
            UuidOutput.Text = sb.ToString().TrimEnd();
        }

        private string FormatUuid(string uuid)
        {
            if (UuidNoDashCheck.IsChecked == true) uuid = uuid.Replace("-", "");
            if (UuidUpperCheck.IsChecked == true) uuid = uuid.ToUpperInvariant();
            return uuid;
        }

        private void GenPwd_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PwdLengthBox.Text.Trim(), out int length) || length < 1 || length > 256)
            {
                PwdOutput.Text = "长度需为 1-256 的整数";
                return;
            }
            if (!int.TryParse(PwdCountBox.Text.Trim(), out int count) || count < 1 || count > 1000)
            {
                PwdOutput.Text = "数量需为 1-1000 的整数";
                return;
            }

            var charset = BuildCharset();
            if (charset.Length == 0)
            {
                PwdOutput.Text = "请至少选择一种字符类型";
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                sb.AppendLine(GeneratePassword(length, charset));
            }
            PwdOutput.Text = sb.ToString().TrimEnd();
        }

        private string BuildCharset()
        {
            var sb = new StringBuilder();
            if (UpperCheck.IsChecked == true) sb.Append(Upper);
            if (LowerCheck.IsChecked == true) sb.Append(Lower);
            if (DigitCheck.IsChecked == true) sb.Append(Digits);
            if (SymbolCheck.IsChecked == true) sb.Append(Symbols);
            return sb.ToString();
        }

        private static string GeneratePassword(int length, string charset)
        {
            var chars = new char[length];
            using (var rng = new RNGCryptoServiceProvider())
            {
                var bytes = new byte[length * 4];
                rng.GetBytes(bytes);
                for (int i = 0; i < length; i++)
                {
                    uint val = BitConverter.ToUInt32(bytes, i * 4);
                    chars[i] = charset[(int)(val % (uint)charset.Length)];
                }
            }
            return new string(chars);
        }

        private void CopyUuid_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(UuidOutput.Text))
            {
                string err;
                ClipboardHelper.TrySetText(UuidOutput.Text, out err);
            }
        }

        private void CopyPwd_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(PwdOutput.Text))
            {
                string err;
                ClipboardHelper.TrySetText(PwdOutput.Text, out err);
            }
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("idgenerator", out state)) return;
            var uuidCount = HistoryManager.GetString(state, "uuidCount");
            var pwdLength = HistoryManager.GetString(state, "pwdLength");
            var pwdCount = HistoryManager.GetString(state, "pwdCount");
            if (uuidCount.Length > 0) UuidCountBox.Text = uuidCount;
            if (pwdLength.Length > 0) PwdLengthBox.Text = pwdLength;
            if (pwdCount.Length > 0) PwdCountBox.Text = pwdCount;
            UpperCheck.IsChecked = HistoryManager.GetBool(state, "upper", true);
            LowerCheck.IsChecked = HistoryManager.GetBool(state, "lower", true);
            DigitCheck.IsChecked = HistoryManager.GetBool(state, "digit", true);
            SymbolCheck.IsChecked = HistoryManager.GetBool(state, "symbol", true);
            UuidOutput.Text = HistoryManager.GetString(state, "uuidOut");
            PwdOutput.Text = HistoryManager.GetString(state, "pwdOut");
            UuidUpperCheck.IsChecked = HistoryManager.GetBool(state, "uuidUpper", false);
            UuidNoDashCheck.IsChecked = HistoryManager.GetBool(state, "uuidNoDash", false);
            int kind = HistoryManager.GetInt(state, "idKind", 0);
            if (kind < 0 || kind > 1) kind = 0;
            IdKindCombo.SelectedIndex = kind;
            ApplyIdKindUi();
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("idgenerator", new Dictionary<string, object>
            {
                { "uuidCount", UuidCountBox.Text ?? "" },
                { "pwdLength", PwdLengthBox.Text ?? "" },
                { "pwdCount", PwdCountBox.Text ?? "" },
                { "upper", UpperCheck.IsChecked == true },
                { "lower", LowerCheck.IsChecked == true },
                { "digit", DigitCheck.IsChecked == true },
                { "symbol", SymbolCheck.IsChecked == true },
                { "uuidOut", UuidOutput.Text ?? "" },
                { "pwdOut", PwdOutput.Text ?? "" },
                { "uuidUpper", UuidUpperCheck.IsChecked == true },
                { "uuidNoDash", UuidNoDashCheck.IsChecked == true },
                { "idKind", IdKindCombo.SelectedIndex }
            });
        }
    }
}
