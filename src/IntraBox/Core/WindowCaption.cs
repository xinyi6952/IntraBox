using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IntraBox.Core
{
    /// <summary>
    /// 让带系统边框的 Window 标题栏跟随浅色/深色主题。
    /// Win10 1809+ 有效；更早系统按能力跳过，不调用也不记「降级」。
    /// </summary>
    public static class WindowCaption
    {
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void Hook()
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            Apply(sender as Window);
        }

        public static void RefreshAll()
        {
            var app = Application.Current;
            if (app == null) return;
            foreach (Window w in app.Windows)
                Apply(w);
        }

        public static void Apply(Window window)
        {
            if (window == null) return;
            if (window.WindowStyle == WindowStyle.None) return;
            if (!WinCompatHelper.SupportsDarkWindowCaption()) return;
            try
            {
                var helper = new WindowInteropHelper(window);
                IntPtr hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero)
                    hwnd = helper.EnsureHandle();
                if (hwnd == IntPtr.Zero) return;

                int useDark = ThemeManager.Resolve(ThemeManager.Current) == ThemeManager.Dark ? 1 : 0;
                int hr = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
                if (hr != 0)
                    hr = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
                if (hr != 0)
                    CompatDegrade.Once("caption-dark", "系统标题栏深色模式不可用（Windows 10 1809 以下会忽略）。", null);
            }
            catch (Exception ex)
            {
                CompatDegrade.Once("caption-dark-ex", "设置标题栏深色模式失败，保持系统标题栏。", ex);
            }
        }
    }
}
