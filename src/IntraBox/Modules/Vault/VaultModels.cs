using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IntraBox.Core;

namespace IntraBox.Modules.Vault
{
    public static class VaultText
    {
        public const string DefaultTitle = "无标题分类";

        public static List<string> ParsePlatforms(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            var buf = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '/' || c == '、' || c == ',' || c == '，' || c == ';' || c == '；' || c == '|')
                {
                    AddPlatform(list, buf.ToString());
                    buf.Length = 0;
                }
                else
                    buf.Append(c);
            }
            AddPlatform(list, buf.ToString());
            return list;
        }

        public static string FormatPlatforms(IList<string> platforms)
        {
            if (platforms == null || platforms.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < platforms.Count; i++)
            {
                string p = platforms[i];
                if (string.IsNullOrEmpty(p)) continue;
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(p);
            }
            return sb.ToString();
        }

        public static string CopyAccountPassword(string account, string password)
        {
            return CopyRecord(null, null, account, password);
        }

        public static string CopyWithPlatform(IList<string> platforms, string account, string password)
        {
            return CopyRecord(platforms, null, account, password);
        }

        /// <summary>
        /// 复制块：平台 / 有则 URL / 账号 / 密码。无 URL 时不输出 URL 行。
        /// </summary>
        public static string CopyRecord(IList<string> platforms, string url, string account, string password)
        {
            var sb = new StringBuilder();
            sb.Append("平台: ");
            sb.Append(FormatPlatforms(platforms));
            url = url != null ? url.Trim() : "";
            if (url.Length > 0)
            {
                sb.Append("\r\nURL: ");
                sb.Append(url);
            }
            sb.Append("\r\n账号: ");
            sb.Append(account ?? "");
            sb.Append("\r\n密码: ");
            sb.Append(password ?? "");
            return sb.ToString();
        }

        /// <summary>分类内全部账号块，记录之间空一行。无明文账号或密码的行跳过。</summary>
        public static string CopyAll(IList<VaultEntry> entries)
        {
            if (entries == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                if (string.IsNullOrWhiteSpace(e.Account) || string.IsNullOrWhiteSpace(e.Password))
                    continue;
                if (sb.Length > 0) sb.Append("\r\n\r\n");
                sb.Append(CopyRecord(e.Platforms, e.Url, e.Account, e.Password));
            }
            return sb.ToString();
        }

        public static List<VaultEntry> ParseExport(string text)
        {
            var result = new List<VaultEntry>();
            if (string.IsNullOrEmpty(text)) return result;
            VaultEntry cur = null;
            bool hasPlat = false, hasAcc = false, hasPwd = false;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (IsBlankLine(line))
                {
                    FlushParse(result, ref cur, ref hasPlat, ref hasAcc, ref hasPwd);
                    continue;
                }
                string key, val;
                if (!TryParseField(line, out key, out val)) continue;
                if (key == "平台")
                {
                    if (hasPlat || hasAcc || hasPwd)
                        FlushParse(result, ref cur, ref hasPlat, ref hasAcc, ref hasPwd);
                    EnsureParse(ref cur);
                    cur.Platforms = ParsePlatforms(val);
                    hasPlat = true;
                }
                else if (key == "url")
                {
                    EnsureParse(ref cur);
                    cur.Url = val;
                }
                else if (key == "账号")
                {
                    if (hasAcc && hasPwd)
                        FlushParse(result, ref cur, ref hasPlat, ref hasAcc, ref hasPwd);
                    EnsureParse(ref cur);
                    cur.Account = val;
                    hasAcc = true;
                }
                else if (key == "密码")
                {
                    EnsureParse(ref cur);
                    cur.Password = val;
                    hasPwd = true;
                }
            }
            FlushParse(result, ref cur, ref hasPlat, ref hasAcc, ref hasPwd);
            return result;
        }

        public static string SafeFileName(string title)
        {
            if (string.IsNullOrEmpty(title)) return DefaultTitle;
            foreach (char c in Path.GetInvalidFileNameChars())
                title = title.Replace(c, '_');
            title = title.Trim();
            return title.Length == 0 ? DefaultTitle : title;
        }

        public static bool MatchesSearch(string title, string remark, IList<VaultEntry> entries, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (Contains(title, query) || Contains(remark, query)) return true;
            if (entries == null) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                if (e.Platforms != null)
                {
                    for (int p = 0; p < e.Platforms.Count; p++)
                    {
                        if (Contains(e.Platforms[p], query)) return true;
                    }
                }
                if (!e.Masked && Contains(e.Account, query)) return true;
                if (Contains(e.Url, query)) return true;
            }
            return false;
        }

        public static int CountEntries(IList<VaultEntry> entries)
        {
            if (entries == null) return 0;
            int n = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsKept(entries[i])) n++;
            }
            return n;
        }

        public static bool HasMasked(IList<VaultEntry> entries)
        {
            if (entries == null) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].Masked) return true;
            }
            return false;
        }

        public static bool IsKept(VaultEntry e)
        {
            if (e == null) return false;
            bool hasPlat = e.Platforms != null && e.Platforms.Count > 0;
            bool hasAcc = !string.IsNullOrWhiteSpace(e.Account) || !string.IsNullOrEmpty(e.AccountEnc);
            bool hasPwd = !string.IsNullOrWhiteSpace(e.Password) || !string.IsNullOrEmpty(e.PasswordEnc);
            return hasPlat || hasAcc || hasPwd;
        }

        public static string ValidateEntry(VaultEntry e)
        {
            if (e == null || !IsKept(e)) return null;
            bool hasAcc = !string.IsNullOrWhiteSpace(e.Account) || !string.IsNullOrEmpty(e.AccountEnc);
            bool hasPwd = !string.IsNullOrWhiteSpace(e.Password) || !string.IsNullOrEmpty(e.PasswordEnc);
            if (!hasAcc || !hasPwd)
                return "每行账号和密码都必填（平台、URL 可空）";
            return null;
        }

        public static void NormalizeEntry(VaultEntry e)
        {
            if (e == null) return;
            if (string.IsNullOrEmpty(e.Id)) e.Id = Guid.NewGuid().ToString("N");
            if (e.Url != null) e.Url = e.Url.Trim();
            if (e.Platforms == null) e.Platforms = new List<string>();
            else
            {
                var cleaned = new List<string>();
                for (int i = 0; i < e.Platforms.Count; i++)
                    AddPlatform(cleaned, e.Platforms[i]);
                e.Platforms = cleaned;
            }
        }

        public static void PackForDisk(VaultEntry e, byte[] key)
        {
            if (e == null) return;
            NormalizeEntry(e);
            if (e.Masked)
            {
                if (key == null)
                    throw new InvalidOperationException("加密行需要先验证或设置查看密码");
                SealFields(e, key);
                e.Account = "";
                e.Password = "";
            }
            else
            {
                e.AccountEnc = null;
                e.PasswordEnc = null;
            }
        }

        /// <summary>用当前明文生成密文字段，不清空内存中的明文。</summary>
        public static void SealFields(VaultEntry e, byte[] key)
        {
            if (e == null || key == null) return;
            e.AccountEnc = VaultCrypto.EncryptString(key, e.Account ?? "");
            e.PasswordEnc = VaultCrypto.EncryptString(key, e.Password ?? "");
        }

        public static bool TryReveal(VaultEntry e, byte[] key, out string error)
        {
            error = null;
            if (e == null) return true;
            bool hasEnc = !string.IsNullOrEmpty(e.AccountEnc) || !string.IsNullOrEmpty(e.PasswordEnc);
            if (!hasEnc) return true;
            if (key == null)
            {
                error = "请先验证查看密码";
                return false;
            }
            try
            {
                if (!string.IsNullOrEmpty(e.AccountEnc))
                    e.Account = VaultCrypto.DecryptString(key, e.AccountEnc);
                if (!string.IsNullOrEmpty(e.PasswordEnc))
                    e.Password = VaultCrypto.DecryptString(key, e.PasswordEnc);
                return true;
            }
            catch (Exception ex)
            {
                error = "解密失败：" + ex.Message;
                return false;
            }
        }

        private static void EnsureParse(ref VaultEntry cur)
        {
            if (cur != null) return;
            cur = new VaultEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Masked = false,
                Platforms = new List<string>()
            };
        }

        private static void FlushParse(List<VaultEntry> result, ref VaultEntry cur,
            ref bool hasPlat, ref bool hasAcc, ref bool hasPwd)
        {
            if (cur != null)
            {
                NormalizeEntry(cur);
                if (IsKept(cur) && ValidateEntry(cur) == null)
                    result.Add(cur);
            }
            cur = null;
            hasPlat = false;
            hasAcc = false;
            hasPwd = false;
        }

        private static bool IsBlankLine(string line)
        {
            return line == null || line.Trim().Length == 0;
        }

        private static bool TryParseField(string line, out string key, out string value)
        {
            key = null;
            value = null;
            if (line == null) return false;
            int colon = line.IndexOf('：');
            int ascii = line.IndexOf(':');
            if (ascii >= 0 && (colon < 0 || ascii < colon)) colon = ascii;
            if (colon <= 0) return false;
            string raw = line.Substring(0, colon).Trim();
            value = line.Substring(colon + 1).Trim();
            if (raw == "平台") key = "平台";
            else if (string.Equals(raw, "URL", StringComparison.OrdinalIgnoreCase)) key = "url";
            else if (raw == "账号" || raw == "帐户") key = "账号";
            else if (raw == "密码") key = "密码";
            else return false;
            return true;
        }

        private static void AddPlatform(List<string> list, string raw)
        {
            if (raw == null) return;
            string t = raw.Trim();
            if (t.Length == 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], t, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(t);
        }

        private static bool Contains(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return false;
            return text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public sealed class VaultIndexFile
    {
        public int Version { get; set; }
        public bool SampleSeeded { get; set; }
        public List<VaultItem> Items { get; set; }
    }

    public sealed class VaultItem
    {
        public string Uid { get; set; }
        public string Title { get; set; }
        public bool Pinned { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int EntryCount { get; set; }
        public bool HasSecret { get; set; }

        public static VaultItem CreateNew(string title)
        {
            var now = DateTime.Now;
            return new VaultItem
            {
                Uid = Guid.NewGuid().ToString("N"),
                Title = title,
                CreatedAt = now,
                UpdatedAt = now
            };
        }
    }

    public sealed class VaultBody
    {
        public int Version { get; set; }
        public string Remark { get; set; }
        public string Salt { get; set; }
        public string Verifier { get; set; }
        public List<VaultEntry> Entries { get; set; }

        public bool HasPassword
        {
            get { return !string.IsNullOrEmpty(Salt) && !string.IsNullOrEmpty(Verifier); }
        }

        public byte[] SaltBytes()
        {
            if (string.IsNullOrEmpty(Salt)) return null;
            try { return Convert.FromBase64String(Salt); }
            catch { return null; }
        }
    }

    public sealed class VaultEntry
    {
        public string Id { get; set; }
        public List<string> Platforms { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string Url { get; set; }
        public bool Masked { get; set; }
        public string AccountEnc { get; set; }
        public string PasswordEnc { get; set; }
    }
}
