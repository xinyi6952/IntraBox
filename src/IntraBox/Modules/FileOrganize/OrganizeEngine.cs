using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace IntraBox.Modules.FileOrganize
{
    /// <summary>一条整理规则：匹配条件 + 目标目录模板（支持 {ext}/{type}/{name}）。</summary>
    public sealed class OrganizeRule
    {
        /// <summary>0 扩展名 1 文件名包含 2 正则 3 大于指定大小(MB) 4 早于指定日期</summary>
        public int Kind { get; set; }
        public string Pattern { get; set; }
        public string TargetTemplate { get; set; }
        /// <summary>未写入 JSON 时默认为启用，兼容旧规则。</summary>
        public bool Enabled { get; set; }

        public OrganizeRule()
        {
            Enabled = true;
        }
    }

    public sealed class OrganizePlanItem
    {
        public string Name { get; set; }
        public string FromPath { get; set; }
        public string ToPath { get; set; }
        public string Folder { get; set; }
        public string Note { get; set; }
        /// <summary>预览勾选。默认 false，只有勾选的才会被移动。</summary>
        public bool Selected { get; set; }
    }

    /// <summary>文件整理匹配与目标路径展开（纯逻辑，便于单测）。</summary>
    public static class OrganizeEngine
    {
        public static string KindName(int kind)
        {
            if (kind == 1) return "文件名包含";
            if (kind == 2) return "正则";
            if (kind == 3) return "大于指定大小";
            if (kind == 4) return "早于指定日期";
            return "扩展名";
        }

        public static string FileCategory(string ext)
        {
            string e = (ext ?? "").Trim().TrimStart('.').ToLowerInvariant();
            if (e == "jpg" || e == "jpeg" || e == "png" || e == "gif" || e == "bmp" || e == "ico"
                || e == "tif" || e == "tiff" || e == "webp" || e == "svg") return "图片";
            if (e == "doc" || e == "docx" || e == "pdf" || e == "txt" || e == "md" || e == "rtf" || e == "odt") return "文档";
            if (e == "xls" || e == "xlsx" || e == "csv") return "表格";
            if (e == "ppt" || e == "pptx") return "演示";
            if (e == "zip" || e == "rar" || e == "7z" || e == "tar" || e == "gz") return "压缩包";
            if (e == "mp3" || e == "wav" || e == "flac" || e == "aac") return "音频";
            if (e == "mp4" || e == "avi" || e == "mkv" || e == "mov") return "视频";
            if (e == "cs" || e == "js" || e == "ts" || e == "py" || e == "java" || e == "go" || e == "rs") return "代码";
            return "其他";
        }

        public static bool Matches(OrganizeRule rule, string path, out string note)
        {
            note = "";
            if (rule == null || string.IsNullOrEmpty(path)) return false;
            string name = Path.GetFileName(path);
            string raw = rule.Pattern ?? "";
            int kind = rule.Kind;
            try
            {
                if (kind == 0)
                {
                    string ext = Path.GetExtension(name);
                    if (string.IsNullOrEmpty(ext)) return false;
                    string want = raw.Trim();
                    if (string.IsNullOrEmpty(want))
                    {
                        note = "扩展名 " + ext.TrimStart('.');
                        return true;
                    }
                    if (want.StartsWith("*.")) want = want.Substring(1);
                    if (!want.StartsWith(".")) want = "." + want;
                    if (!ext.Equals(want, StringComparison.OrdinalIgnoreCase)) return false;
                    note = "扩展名 " + ext;
                    return true;
                }
                if (kind == 1)
                {
                    if (string.IsNullOrEmpty(raw)) return false;
                    if (name.IndexOf(raw, StringComparison.OrdinalIgnoreCase) < 0) return false;
                    note = "文件名包含 " + raw;
                    return true;
                }
                if (kind == 2)
                {
                    if (string.IsNullOrEmpty(raw)) return false;
                    if (!Regex.IsMatch(name, raw, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
                    note = "正则";
                    return true;
                }
                if (kind == 3)
                {
                    double mb;
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out mb))
                        double.TryParse(raw, out mb);
                    long limit = (long)(mb * 1024 * 1024);
                    long len = new FileInfo(path).Length;
                    if (len <= limit) return false;
                    note = "大于 " + mb.ToString("0.##") + " MB";
                    return true;
                }
                if (kind == 4)
                {
                    DateTime limit;
                    if (!DateTime.TryParse(raw, out limit)) return false;
                    if (File.GetLastWriteTime(path) >= limit) return false;
                    note = "早于 " + limit.ToString("yyyy-MM-dd");
                    return true;
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        /// <summary>把模板展开成相对目录（不含文件名）。非法字符替换为下划线。</summary>
        public static string ExpandFolder(string template, string filePath)
        {
            string t = (template ?? "").Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            t = t.TrimEnd(Path.DirectorySeparatorChar, '/');
            if (string.IsNullOrEmpty(t)) t = "整理";

            string name = Path.GetFileNameWithoutExtension(filePath ?? "");
            string ext = Path.GetExtension(filePath ?? "").TrimStart('.');
            DateTime dt = DateTime.Now;
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    dt = File.GetLastWriteTime(filePath);
            }
            catch { }

            t = ReplaceToken(t, "{ext}", Sanitize(ext));
            t = ReplaceToken(t, "{name}", Sanitize(name));
            t = ReplaceToken(t, "{type}", Sanitize(FileCategory(ext)));
            t = ReplaceToken(t, "{date}", dt.ToString("yyyy-MM-dd"));
            t = ReplaceToken(t, "{yyyy}", dt.ToString("yyyy"));
            t = ReplaceToken(t, "{MM}", dt.ToString("MM"));
            t = ReplaceToken(t, "{dd}", dt.ToString("dd"));
            return t;
        }

        /// <summary>
        /// 整理目标相对目录：根目录为给定日期（yyyy-MM-dd），子目录为规则里的目标目录。
        /// 规则里的 {date}/{yyyy}/{MM}/{dd} 不再作为根，以免和当天日期重复。
        /// </summary>
        public static string DestFolder(string template, string filePath, DateTime now)
        {
            string t = template ?? "";
            t = t.Replace("{date}", "").Replace("{yyyy}", "").Replace("{MM}", "").Replace("{dd}", "");
            t = t.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string dbl = Path.DirectorySeparatorChar.ToString() + Path.DirectorySeparatorChar;
            while (t.IndexOf(dbl, StringComparison.Ordinal) >= 0)
                t = t.Replace(dbl, Path.DirectorySeparatorChar.ToString());
            t = t.Trim().Trim(Path.DirectorySeparatorChar, '/');

            string sub = string.IsNullOrEmpty(t) ? "" : ExpandFolder(t, filePath);
            string root = now.ToString("yyyy-MM-dd");
            if (string.IsNullOrEmpty(sub)) return root;
            return Path.Combine(root, sub);
        }

        public static List<OrganizePlanItem> BuildPlan(string sourceDir, bool includeSub,
            IList<OrganizeRule> rules)
        {
            var list = new List<OrganizePlanItem>();
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir) || rules == null)
                return list;

            var option = includeSub ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] files;
            try { files = Directory.GetFiles(sourceDir, "*", option); }
            catch { return list; }

            foreach (var path in files)
            {
                OrganizeRule hit = null;
                string note = "";
                for (int i = 0; i < rules.Count; i++)
                {
                    if (rules[i] == null || !rules[i].Enabled) continue;
                    string n;
                    if (Matches(rules[i], path, out n))
                    {
                        hit = rules[i];
                        note = n;
                        break;
                    }
                }
                if (hit == null) continue;

                string folder = DestFolder(hit.TargetTemplate, path, DateTime.Now);
                string destDir = Path.Combine(sourceDir, folder);
                string dest = Path.Combine(destDir, Path.GetFileName(path));
                if (path.Equals(dest, StringComparison.OrdinalIgnoreCase)) continue;
                if (path.StartsWith(destDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || path.Equals(destDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                list.Add(new OrganizePlanItem
                {
                    Name = Path.GetFileName(path),
                    FromPath = path,
                    ToPath = dest,
                    Folder = folder,
                    Note = note
                });
            }
            return list;
        }

        private static string ReplaceToken(string s, string token, string value)
        {
            return s.Replace(token, value ?? "");
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var chars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
                s = s.Replace(chars[i], '_');
            return s;
        }
    }
}
