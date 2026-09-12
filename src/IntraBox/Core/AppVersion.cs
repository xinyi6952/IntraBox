using System;
using System.Reflection;

namespace IntraBox.Core
{
    /// <summary>
    /// 产品版本：四段有序数字 主.次.修订.打包。
    /// 窗口标题、托盘、设置、欢迎页与 exe 属性都用同一串（如 1.0.0.312）。
    /// 与 config/launcher 等数据格式版本无关。
    /// </summary>
    public static class AppVersion
    {
        public static Version Current
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                object[] attrs = asm.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false);
                if (attrs != null && attrs.Length > 0)
                {
                    Version parsed;
                    if (TryParse(((AssemblyFileVersionAttribute)attrs[0]).Version, out parsed))
                        return parsed;
                }
                Version name = asm.GetName().Version;
                return name ?? new Version(0, 0, 0, 0);
            }
        }

        /// <summary>界面与文件属性同一串四段号，如 1.0.0.312。</summary>
        public static string Display
        {
            get { return Full; }
        }

        /// <summary>四段全号，如 1.0.0.312。</summary>
        public static string Full
        {
            get { return FormatFull(Current); }
        }

        public static string ProductTitle
        {
            get { return "IntraBox " + Display; }
        }

        public static bool TryParse(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            Version v;
            if (!Version.TryParse(text.Trim(), out v)) return false;
            version = Normalize(v);
            return true;
        }

        public static Version Normalize(Version v)
        {
            if (v == null) return new Version(0, 0, 0, 0);
            int build = v.Build < 0 ? 0 : v.Build;
            int rev = v.Revision < 0 ? 0 : v.Revision;
            return new Version(v.Major, v.Minor, build, rev);
        }

        public static string FormatDisplay(Version v)
        {
            return FormatFull(v);
        }

        public static string FormatFull(Version v)
        {
            v = Normalize(v);
            return v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
        }

        /// <summary>打包序号 +1；该段到 65535 时进位到修订/次/主版本。</summary>
        public static Version BumpPack(Version v)
        {
            v = Normalize(v);
            if (v.Revision < 65535)
                return new Version(v.Major, v.Minor, v.Build, v.Revision + 1);
            if (v.Build < 65535)
                return new Version(v.Major, v.Minor, v.Build + 1, 0);
            if (v.Minor < 65535)
                return new Version(v.Major, v.Minor + 1, 0, 0);
            return new Version(v.Major + 1, 0, 0, 0);
        }

        public static int Compare(Version a, Version b)
        {
            return Normalize(a).CompareTo(Normalize(b));
        }
    }
}
