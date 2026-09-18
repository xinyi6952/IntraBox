using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Windows.Media;
using Microsoft.Win32;

namespace IntraBox.Core
{
    /// <summary>
    /// Win7 SP1 上仍可用的兼容处理：TLS、IE 文档模式、DPI、OCR 原生库、WebView2 路径、系统版本。
    /// </summary>
    public static class WinCompatHelper
    {
        public const int Tls11Flag = 768;
        public const int Tls12Flag = 3072;
        public const int Tls13Flag = 12288;
        public const string OcrVcRuntimeHint =
            "无法加载 OCR 组件。请安装 Visual C++ 2015-2019 运行库（x64）。";
        public const string WebView2FolderName = "WebView2Runtime";
        public const string WebView2ExeName = "msedgewebview2.exe";

        /// <summary>
        /// 按系统打开可用的最高 TLS：Win10/11 含 1.3，Win7 只开到 1.2。不在旧系统上先试失败再降级。
        /// </summary>
        public static void EnableModernTls()
        {
            if (IsWindows10OrNewer())
            {
                try
                {
                    ServicePointManager.SecurityProtocol =
                        SecurityProtocolType.Tls
                        | (SecurityProtocolType)Tls11Flag
                        | (SecurityProtocolType)Tls12Flag
                        | (SecurityProtocolType)Tls13Flag;
                    return;
                }
                catch (Exception ex)
                {
                    CompatDegrade.Once("tls13", "本机不支持在 .NET 中开启 TLS 1.3，改用 TLS 1.2。", ex);
                }
            }
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls
                    | (SecurityProtocolType)Tls11Flag
                    | (SecurityProtocolType)Tls12Flag;
            }
            catch (Exception ex)
            {
                CompatDegrade.Once("tls12", "无法设置 TLS 1.2，HTTPS 将使用系统默认协议。", ex);
            }
        }

        /// <summary>
        /// IE 版本 → FEATURE_BROWSER_EMULATION。优先 svcVersion（IE11 的 Version 仍是 9.11）。
        /// 读不到版本时按 Win7 自带 IE8。
        /// </summary>
        public static int BrowserEmulationDword(string svcVersion, string version)
        {
            int major = ParseMajor(svcVersion);
            if (major <= 0) major = ParseMajor(version);
            if (major >= 11) return 11001;
            if (major >= 10) return 10000;
            if (major >= 9) return 9000;
            if (major >= 8) return 8000;
            if (major >= 7) return 7000;
            return 8000;
        }

        public static int ReadBrowserEmulationDword()
        {
            string svc;
            string ver;
            if (!TryReadIeVersion(RegistryView.Registry32, out svc, out ver))
                TryReadIeVersion(RegistryView.Registry64, out svc, out ver);
            return BrowserEmulationDword(svc, ver);
        }

        public static double PixelsPerDip(Visual visual)
        {
            if (visual == null) return 1.0;
            try { return VisualTreeHelper.GetDpi(visual).PixelsPerDip; }
            catch (Exception ex)
            {
                CompatDegrade.Once("dpi-ppd", "读取 PixelsPerDip 失败，已按 1.0。", ex);
                return 1.0;
            }
        }

        /// <summary>屏幕每英寸像素（约 96/120/144）。失败回 96。给 ScreenDipSize 用，不是 PixelsPerDip。</summary>
        public static void GetPixelsPerInch(Visual visual, out double dpiX, out double dpiY)
        {
            dpiX = 96;
            dpiY = 96;
            if (visual == null) return;
            try
            {
                var d = VisualTreeHelper.GetDpi(visual);
                if (d.PixelsPerInchX >= 1) dpiX = d.PixelsPerInchX;
                if (d.PixelsPerInchY >= 1) dpiY = d.PixelsPerInchY;
            }
            catch (Exception ex)
            {
                CompatDegrade.Once("dpi-ppi", "读取屏幕 DPI 失败，已按 96DPI 显示。", ex);
            }
        }

        /// <summary>Win8 起（含 shim 成 6.2 时看 CurrentBuildNumber ≥ 9200）。护眼全屏校色等。</summary>
        public static bool IsWindows8OrNewer()
        {
            try
            {
                Version v = Environment.OSVersion.Version;
                if (v.Major > 6) return true;
                if (v.Major == 6 && v.Minor >= 2) return true;
            }
            catch { }
            return ReadCurrentBuildNumber() >= 9200;
        }

        /// <summary>Win10 1809 起（build ≥ 17763）才有 immersive 深色标题栏。</summary>
        public static bool SupportsDarkWindowCaption()
        {
            if (!IsWindows10OrNewer()) return false;
            int build = ReadCurrentBuildNumber();
            if (build <= 0) return true;
            return build >= 17763;
        }

        /// <summary>Win10 1507 起（含被兼容 shim 压成 6.2 时，改看 CurrentBuildNumber）。</summary>
        public static bool IsWindows10OrNewer()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10) return true;
            }
            catch { }
            int build = ReadCurrentBuildNumber();
            return build >= 10240;
        }

        public static int ReadCurrentBuildNumber()
        {
            string s = ReadNtCurrent("CurrentBuildNumber");
            if (string.IsNullOrEmpty(s)) s = ReadNtCurrent("CurrentBuild");
            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return 0;
        }

        /// <summary>exe 旁 WebView2Runtime（或其子目录）里有 msedgewebview2.exe 则返回该目录。</summary>
        public static string FindWebView2FixedFolder()
        {
            return FindWebView2FixedFolder(AppDomain.CurrentDomain.BaseDirectory);
        }

        public static string FindWebView2FixedFolder(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return null;
            string root = Path.Combine(baseDir, WebView2FolderName);
            return FindWebView2ExeDir(root);
        }

        public static string FindWebView2ExeDir(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
            if (File.Exists(Path.Combine(root, WebView2ExeName))) return Path.GetFullPath(root);
            try
            {
                string[] dirs = Directory.GetDirectories(root);
                for (int i = 0; i < dirs.Length; i++)
                {
                    if (File.Exists(Path.Combine(dirs[i], WebView2ExeName)))
                        return Path.GetFullPath(dirs[i]);
                }
            }
            catch (Exception ex)
            {
                CompatDegrade.Once("wv2-scan", "扫描 WebView2Runtime 目录失败。", ex);
            }
            return null;
        }

        public static bool LooksLikeMissingVcRuntime(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is DllNotFoundException || e is BadImageFormatException)
                    return true;
                string m = e.Message ?? "";
                if (ContainsNativeDllName(m)) return true;
            }
            return false;
        }

        private static string ReadNtCurrent(string name)
        {
            try
            {
                var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key == null) return "";
                    object v = key.GetValue(name);
                    return v == null ? "" : v.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        private static bool ContainsNativeDllName(string m)
        {
            if (m.IndexOf("tesseract41", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (m.IndexOf("leptonica", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (m.IndexOf("vcruntime", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (m.IndexOf("msvcp140", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool TryReadIeVersion(RegistryView view, out string svc, out string ver)
        {
            svc = null;
            ver = null;
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Internet Explorer"))
                {
                    if (key == null) return false;
                    object a = key.GetValue("svcVersion");
                    object b = key.GetValue("Version");
                    svc = a == null ? null : a.ToString();
                    ver = b == null ? null : b.ToString();
                    return !string.IsNullOrEmpty(svc) || !string.IsNullOrEmpty(ver);
                }
            }
            catch
            {
                return false;
            }
        }

        private static int ParseMajor(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return 0;
            string t = v.Trim();
            int i = 0;
            while (i < t.Length && t[i] >= '0' && t[i] <= '9') i++;
            if (i == 0) return 0;
            int n;
            if (!int.TryParse(t.Substring(0, i), NumberStyles.None, CultureInfo.InvariantCulture, out n))
                return 0;
            return n;
        }
    }
}
