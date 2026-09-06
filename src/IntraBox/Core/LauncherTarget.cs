using System;
using System.IO;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>启动器收藏目标校验：程序 / 文件夹 / 文件 / 内网 URL。不发起网络请求。</summary>
    public static class LauncherTarget
    {
        public const string KindApp = "app";
        public const string KindFolder = "folder";
        public const string KindFile = "file";
        public const string KindUrl = "url";

        public static string KindLabel(string kind)
        {
            if (kind == KindApp) return "程序";
            if (kind == KindFolder) return "文件夹";
            if (kind == KindFile) return "文件";
            if (kind == KindUrl) return "内网地址";
            return kind ?? "";
        }

        public static bool IsBlockedScript(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path.Trim());
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            return ext == ".cmd" || ext == ".vbs" || ext == ".ps1";
        }

        public static bool IsBatPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path.Trim());
            if (string.IsNullOrEmpty(ext)) return false;
            return ext.ToLowerInvariant() == ".bat";
        }

        public static bool IsAllowedUrl(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;
            string t = target.Trim();
            if (t.Length <= 8) return false;
            return t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAppPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path.Trim());
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            return ext == ".exe" || ext == ".lnk" || ext == ".bat";
        }

        /// <summary>校验收藏目标。成功时 error 为空。</summary>
        public static bool TryValidate(string kind, string target, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(kind))
            {
                error = "请选择类型。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(target))
            {
                error = "请填写目标。";
                return false;
            }
            string t = target.Trim();
            if (kind == KindUrl)
            {
                if (!IsAllowedUrl(t))
                {
                    error = "内网地址须以 http:// 或 https:// 开头。";
                    return false;
                }
                return true;
            }
            if (IsBlockedScript(t))
            {
                error = "不支持打开 .cmd / .vbs / .ps1。";
                return false;
            }
            if (kind == KindApp && !IsAppPath(t))
            {
                error = "程序请选择 .exe、.lnk 或 .bat。";
                return false;
            }
            if (kind != KindApp && kind != KindFolder && kind != KindFile)
            {
                error = "未知类型。";
                return false;
            }
            return true;
        }

        /// <summary>文件类收藏的打开方式：空表示系统默认；非空须为 .exe / .lnk 且非脚本。</summary>
        public static bool TryValidateOpenWith(string kind, string openWith, out string error)
        {
            error = "";
            if (kind != KindFile || string.IsNullOrWhiteSpace(openWith))
                return true;
            string t = openWith.Trim();
            if (IsBlockedScript(t) || IsBatPath(t))
            {
                error = "打开方式不支持 .bat / .cmd / .vbs / .ps1。";
                return false;
            }
            if (!IsAppPath(t))
            {
                error = "打开方式请选择 .exe 或 .lnk。";
                return false;
            }
            return true;
        }

        /// <summary>程序且未关闭确认时才二次确认。confirmOpen 缺省（null）视为开启。</summary>
        public static bool NeedsOpenConfirm(string kind, bool? confirmOpen)
        {
            if (kind != KindApp) return false;
            return confirmOpen != false;
        }

        public static string OpenWithLabel(string kind, string openWith)
        {
            if (kind != KindFile) return "";
            if (string.IsNullOrWhiteSpace(openWith)) return "默认";
            try
            {
                string name = Path.GetFileName(openWith.Trim());
                return string.IsNullOrEmpty(name) ? openWith.Trim() : name;
            }
            catch
            {
                return openWith.Trim();
            }
        }

        /// <summary>用 shell32 FindExecutable 查文件的默认打开程序，不发起网络请求。</summary>
        public static string DefaultHandlerHint(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return "系统默认";
            string path = filePath.Trim();
            try
            {
                if (!File.Exists(path))
                    return "系统默认（文件尚不存在）";
            }
            catch
            {
                return "系统默认（文件尚不存在）";
            }
            string exe;
            if (!TryFindAssociatedExe(path, out exe))
                return "系统默认（未检测到关联程序）";
            string name;
            try { name = Path.GetFileNameWithoutExtension(exe); }
            catch { name = exe; }
            if (string.IsNullOrEmpty(name)) name = exe;
            return "默认：" + name;
        }

        public static bool TryFindAssociatedExe(string filePath, out string exePath)
        {
            exePath = "";
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            try
            {
                var sb = new StringBuilder(260);
                int rc = Win32Native.FindExecutable(filePath.Trim(), null, sb);
                if (rc <= 32) return false;
                string p = sb.ToString();
                if (string.IsNullOrWhiteSpace(p)) return false;
                exePath = p.Trim();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string QuoteArg(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\"\"";
            if (path.IndexOf('"') >= 0)
                path = path.Replace("\"", "\\\"");
            return "\"" + path + "\"";
        }

        public const string Uncategorized = "未分类";

        /// <summary>空或空白视为未分类（存空串）。</summary>
        public static string NormalizeCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return "";
            return category.Trim();
        }

        public static string CategoryLabel(string category)
        {
            string n = NormalizeCategory(category);
            return n.Length == 0 ? Uncategorized : n;
        }
    }
}
