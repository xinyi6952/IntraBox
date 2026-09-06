using IntraBox.Core;
using IntraBox.Modules.ClipboardHistory;

namespace IntraBox.Modules.Vault
{
    public static class VaultClipboard
    {
        public static bool TryCopy(string text, out string error)
        {
            bool record = false;
            try
            {
                record = ConfigManager.Instance.Settings.ClipboardRecordVaultCopies;
            }
            catch { }
            if (!record && text != null)
                ClipboardStore.LastText = text;
            return ClipboardHelper.TrySetText(text, out error);
        }
    }
}
