using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IntraBox.Core
{
    /// <summary>
    /// 让带系统边框的 Window 标题栏跟随浅色/深色主题。
    /// Win10 1809+ 有效；Win7 无 immersive dark mode，调用失败则忽略。
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
            try
            {
                var helper = new WindowInteropHelper(window);
                IntPtr hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero)
                    hwnd = helper.EnsureHandle();
                if (hwnd == IntPtr.Zero) return;

                int useDark = ThemeManager.Resolve(ThemeManager.Current) == ThemeManager.Dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
            }
            catch
            {
                // Win7 / 无 DWM：保持系统标题栏
            }
        }
    }
}
