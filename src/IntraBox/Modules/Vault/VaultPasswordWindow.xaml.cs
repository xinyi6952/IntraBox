using System.Windows;
using IntraBox.Core;

namespace IntraBox.Modules.Vault
{
    public partial class VaultPasswordWindow : Window
    {
        private enum Mode
        {
            Unlock,
            Set,
            Change
        }

        private Mode _mode;
        private byte[] _salt;
        private string _verifier;

        public byte[] Key { get; private set; }
        public byte[] OldKey { get; private set; }
        public byte[] Salt { get; private set; }
        public string Verifier { get; private set; }

        public VaultPasswordWindow()
        {
            InitializeComponent();
        }

        public static bool TryUnlock(Window owner, byte[] salt, string verifier, out byte[] key)
        {
            key = null;
            var w = new VaultPasswordWindow();
            w.Owner = owner;
            w._mode = Mode.Unlock;
            w._salt = salt;
            w._verifier = verifier;
            w.Title = "查看密码";
            w.HintText.Text = "输入本分类的查看密码后才能查看、复制或取消加密。";
            w.OldPanel.Visibility = Visibility.Collapsed;
            w.ConfirmPanel.Visibility = Visibility.Collapsed;
            w.NewLabel.Text = "查看密码";
            if (w.ShowDialog() != true) return false;
            key = w.Key;
            return key != null;
        }

        public static bool TrySetNew(Window owner, out byte[] salt, out byte[] key, out string verifier)
        {
            salt = null;
            key = null;
            verifier = null;
            var w = new VaultPasswordWindow();
            w.Owner = owner;
            w._mode = Mode.Set;
            w.Title = "设置查看密码";
            w.HintText.Text = "用于查看和复制加密行。忘记后无法找回，加密行将无法再解密。";
            w.OldPanel.Visibility = Visibility.Collapsed;
            if (w.ShowDialog() != true) return false;
            salt = w.Salt;
            key = w.Key;
            verifier = w.Verifier;
            return key != null;
        }

        public static bool TryChange(Window owner, byte[] oldSalt, string oldVerifier,
            out byte[] salt, out byte[] key, out string verifier, out byte[] oldKey)
        {
            salt = null;
            key = null;
            verifier = null;
            oldKey = null;
            var w = new VaultPasswordWindow();
            w.Owner = owner;
            w._mode = Mode.Change;
            w._salt = oldSalt;
            w._verifier = oldVerifier;
            w.Title = "修改查看密码";
            w.HintText.Text = "修改后所有加密行将用新密码重新加密。忘记新密码同样无法找回。";
            w.OldPanel.Visibility = Visibility.Visible;
            w.NewLabel.Text = "新查看密码";
            if (w.ShowDialog() != true) return false;
            salt = w.Salt;
            key = w.Key;
            verifier = w.Verifier;
            oldKey = w.OldKey;
            return key != null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = "";
            if (_mode == Mode.Unlock)
            {
                string pwd = NewBox.Password ?? "";
                if (pwd.Length == 0)
                {
                    ErrorText.Text = "请填写查看密码";
                    return;
                }
                byte[] key;
                string err = DeriveAndCheck(_salt, _verifier, pwd, out key);
                if (err != null)
                {
                    ErrorText.Text = err;
                    return;
                }
                Key = key;
                DialogResult = true;
                return;
            }

            if (_mode == Mode.Change)
            {
                string oldPwd = OldBox.Password ?? "";
                byte[] oldKey;
                string err = DeriveAndCheck(_salt, _verifier, oldPwd, out oldKey);
                if (err != null)
                {
                    ErrorText.Text = err;
                    return;
                }
                OldKey = oldKey;
            }

            string a = NewBox.Password ?? "";
            string b = ConfirmBox.Password ?? "";
            if (a.Length < 4)
            {
                ErrorText.Text = "查看密码至少 4 个字符";
                return;
            }
            if (a != b)
            {
                ErrorText.Text = "两次输入不一致";
                return;
            }
            byte[] salt = VaultCrypto.NewSalt();
            byte[] key2 = VaultCrypto.DeriveKey(a, salt);
            Salt = salt;
            Key = key2;
            Verifier = VaultCrypto.MakeVerifier(key2);
            DialogResult = true;
        }

        private static string DeriveAndCheck(byte[] salt, string verifier, string password, out byte[] key)
        {
            key = null;
            if (salt == null || string.IsNullOrEmpty(verifier))
                return "尚未设置查看密码";
            try
            {
                key = VaultCrypto.DeriveKey(password, salt);
            }
            catch (System.Exception ex)
            {
                return ex.Message;
            }
            if (!VaultCrypto.CheckVerifier(key, verifier))
            {
                key = null;
                return "查看密码不正确";
            }
            return null;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
