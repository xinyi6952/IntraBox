using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace IntraBox.Core
{
    /// <summary>
    /// 工具状态记忆：只保存数据、不缓存控件。会话内 LRU 字典，并落盘到程序目录 history.json。
    /// 单条字符串超过 MaxTextChars（512KB）直接丢弃，不落盘图片或其它大对象。
    /// </summary>
    public static class HistoryManager
    {
        public const int MaxTextChars = 512 * 1024;
        public const int MaxEntries = 20;

        private static readonly Dictionary<string, Dictionary<string, object>> _store =
            new Dictionary<string, Dictionary<string, object>>();

        private static readonly List<string> _lru = new List<string>();

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json"); }
        }

        public static void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var json = File.ReadAllText(FilePath);
                var map = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(json);
                if (map == null) return;
                _store.Clear();
                _lru.Clear();
                foreach (var kv in map)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                    var copy = new Dictionary<string, object>();
                    foreach (var f in kv.Value)
                    {
                        var text = f.Value as string;
                        if (text != null && text.Length > MaxTextChars) continue;
                        copy[f.Key] = Unwrap(f.Value);
                    }
                    _store[kv.Key] = copy;
                    _lru.Add(kv.Key);
                }
                Evict();
            }
            catch
            {
                _store.Clear();
                _lru.Clear();
            }
        }

        public static void Save(string moduleKey, Dictionary<string, object> fields)
        {
            if (string.IsNullOrEmpty(moduleKey) || fields == null) return;

            var copy = new Dictionary<string, object>();
            foreach (var kv in fields)
            {
                var text = kv.Value as string;
                if (text != null && text.Length > MaxTextChars)
                    continue;
                copy[kv.Key] = kv.Value;
            }

            _store[moduleKey] = copy;
            Touch(moduleKey);
            Evict();
            Persist();
        }

        public static bool TryLoad(string moduleKey, out Dictionary<string, object> fields)
        {
            Dictionary<string, object> stored;
            if (!string.IsNullOrEmpty(moduleKey) && _store.TryGetValue(moduleKey, out stored))
            {
                Touch(moduleKey);
                fields = new Dictionary<string, object>(stored);
                return true;
            }
            fields = null;
            return false;
        }

        public static string GetString(Dictionary<string, object> fields, string key)
        {
            object v;
            if (fields == null || !fields.TryGetValue(key, out v) || v == null) return "";
            return v as string ?? Convert.ToString(v) ?? "";
        }

        public static int GetInt(Dictionary<string, object> fields, string key, int fallback)
        {
            object v;
            if (fields == null || !fields.TryGetValue(key, out v) || v == null) return fallback;
            if (v is int) return (int)v;
            if (v is long) return (int)(long)v;
            if (v is double) return (int)(double)v;
            int n;
            if (int.TryParse(Convert.ToString(v), out n)) return n;
            return fallback;
        }

        public static bool GetBool(Dictionary<string, object> fields, string key, bool fallback)
        {
            object v;
            if (fields == null || !fields.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            bool b;
            if (bool.TryParse(Convert.ToString(v), out b)) return b;
            return fallback;
        }

        private static object Unwrap(object v)
        {
            var token = v as Newtonsoft.Json.Linq.JValue;
            if (token != null) return token.Value;
            return v;
        }

        private static void Persist()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_store);
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        private static void Touch(string key)
        {
            _lru.Remove(key);
            _lru.Add(key);
        }

        private static void Evict()
        {
            while (_lru.Count > MaxEntries)
            {
                var oldest = _lru[0];
                _lru.RemoveAt(0);
                _store.Remove(oldest);
            }
        }
    }
}
