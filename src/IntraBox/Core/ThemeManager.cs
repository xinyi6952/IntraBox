using System;
using System.Windows;
using Microsoft.Win32;

namespace IntraBox.Core
{
    /// <summary>
    /// 浅色 / 深色 / 跟随系统。只替换 MergedDictionaries 中的自研色板，不加载第三方全量 Theme。
    /// 参考 VS Code / DevToys：色板热替换，导航与状态栏用 DynamicResource 跟随。
    /// </summary>
    public static class ThemeManager
    {
        public const string Light = "Light";
        public const string Dark = "Dark";
        public const string System = "System";

        public static string Current { get; private set; } = Dark;

        public static event EventHandler Changed;

        private static bool _watchingSystem;

        public static void Apply(string name)
        {
            if (name != Light && name != Dark && name != System)
                name = Dark;

            Current = name;
            WatchSystem(name == System);

            var resolved = Resolve(name);
            var uri = resolved == Light
                ? new Uri("pack://application:,,,/Themes/LightTheme.xaml", UriKind.Absolute)
                : new Uri("pack://application:,,,/Themes/DarkTheme.xaml", UriKind.Absolute);

            var dict = new ResourceDictionary { Source = uri };
            var merged = Application.Current.Resources.MergedDictionaries;
            if (merged.Count == 0)
                merged.Add(dict);
            else
                merged[0] = dict;

            bool persist = ConfigManager.Instance.Settings.Theme != name;
            ConfigManager.Instance.Settings.Theme = name;
            if (persist) ConfigManager.Instance.Save();
            Changed?.Invoke(null, EventArgs.Empty);
            WindowCaption.RefreshAll();
        }

        /// <summary>把配置值解析成实际色板。Win7 无「浅色应用」注册表项时跟随系统视为深色。</summary>
        public static string Resolve(string name)
        {
            if (name == Light) return Light;
            if (name == System) return IsSystemLight() ? Light : Dark;
            return Dark;
        }

        public static bool IsSystemLight()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return false;
                    object v = key.GetValue("AppsUseLightTheme");
                    if (v is int) return (int)v != 0;
                }
            }
            catch { }
            return false;
        }

        private static void WatchSystem(bool on)
        {
            if (on == _watchingSystem) return;
            if (on)
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            else
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _watchingSystem = on;
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Current != System) return;
            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() => Apply(System)));
        }
    }
}
