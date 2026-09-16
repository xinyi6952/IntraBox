using System;
using System.Globalization;
using System.Net;
using System.Windows.Media;
using Microsoft.Win32;

namespace IntraBox.Core
{
    /// <summary>
    /// Win7 SP1 上仍可用的兼容处理：TLS 1.2、按本机 IE 写 WebBrowser 文档模式、DPI、OCR 原生库提示。
    /// </summary>
    public static class WinCompatHelper
    {
        public const int Tls11Flag = 768;
        public const int Tls12Flag = 3072;
        public const string OcrVcRuntimeHint =
            "无法加载 OCR 组件。请安装 Visual C++ 2015-2019 运行库（x64）。";

        /// <summary>尽量打开 TLS 1.0/1.1/1.2。Win7 未打补丁时枚举可能抛错，忽略后仍走系统默认。</summary>
        public static void EnableModernTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls
                    | (SecurityProtocolType)Tls11Flag
                    | (SecurityProtocolType)Tls12Flag;
            }
            catch { }
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
            catch { return 1.0; }
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
