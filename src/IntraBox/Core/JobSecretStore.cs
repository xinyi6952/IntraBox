using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace IntraBox.Core
{
    /// <summary>编排密钥本机记忆：Windows DPAPI CurrentUser，落盘 jobs\{uid}\secrets.bin；明文永不进 index.json。</summary>
    public static class JobSecretStore
    {
        private sealed class SecretFile
        {
            public int FormatVersion { get; set; }
            public Dictionary<string, string> Values { get; set; }
        }

        public static string StorePath(string jobUid)
        {
            return Path.Combine(DataPaths.JobsDir, jobUid ?? "_", "secrets.bin");
        }

        /// <summary>读取已记住的密钥；DPAPI 失败时返回空表并给出警告。</summary>
        public static Dictionary<string, string> Load(string jobUid, out string warning)
        {
            warning = null;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(jobUid)) return map;
            string path = StorePath(jobUid);
            if (!File.Exists(path)) return map;
            try
            {
                byte[] blob = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(blob, Entropy(jobUid), DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plain);
                var file = JsonConvert.DeserializeObject<SecretFile>(json);
                if (file == null || file.Values == null) return map;
                foreach (var kv in file.Values)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    map[kv.Key] = kv.Value ?? "";
                }
            }
            catch
            {
                warning = "已记住的密钥无法解密（可能换过 Windows 用户或配置损坏），已忽略";
                map.Clear();
            }
            return map;
        }

        /// <summary>合并写入指定密钥名；值为空则删除该项。全部空则删除文件。</summary>
        public static bool Save(string jobUid, IDictionary<string, string> secrets, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(jobUid))
            {
                error = "编排编号无效";
                return false;
            }
            try
            {
                var existing = Load(jobUid, out _);
                if (secrets != null)
                {
                    foreach (var kv in secrets)
                    {
                        if (string.IsNullOrEmpty(kv.Key)) continue;
                        if (string.IsNullOrEmpty(kv.Value))
                            existing.Remove(kv.Key);
                        else
                            existing[kv.Key] = kv.Value;
                    }
                }
                string path = StorePath(jobUid);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                if (existing.Count == 0)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    return true;
                }
                var file = new SecretFile { FormatVersion = 1, Values = existing };
                string json = JsonConvert.SerializeObject(file);
                byte[] plain = Encoding.UTF8.GetBytes(json);
                byte[] blob = ProtectedData.Protect(plain, Entropy(jobUid), DataProtectionScope.CurrentUser);
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, blob);
                File.Copy(tmp, path, true);
                try { File.Delete(tmp); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                error = "保存密钥失败：" + ex.RootMessage();
                return false;
            }
        }

        public static bool Clear(string jobUid, out string error)
        {
            error = null;
            try
            {
                string path = StorePath(jobUid);
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.RootMessage();
                return false;
            }
        }

        private static byte[] Entropy(string jobUid)
        {
            return Encoding.UTF8.GetBytes("IntraBox.JobSecret." + (jobUid ?? ""));
        }
    }
}
