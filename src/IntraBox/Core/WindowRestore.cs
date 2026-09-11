using System.Windows;

namespace IntraBox.Core
{
    /// <summary>主窗口显示/隐藏时恢复或记住最大化状态。</summary>
    public static class WindowRestore
    {
        public static void ShowAndRestore(Window w)
        {
            if (w == null) return;
            w.Show();
            w.WindowState = ConfigManager.Instance.Settings.WindowMaximized
                ? WindowState.Maximized
                : WindowState.Normal;
            w.Activate();
            w.Topmost = true;
            w.Topmost = false;
            w.Focus();
            MemoryIdleGuard.NotifyResumed();
        }

        public static void PersistFrom(Window w)
        {
            if (w == null) return;
            ConfigManager.Instance.Settings.WindowMaximized = w.WindowState == WindowState.Maximized;
            ConfigManager.Instance.Save();
        }
    }
}
