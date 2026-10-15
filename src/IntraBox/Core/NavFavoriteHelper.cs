using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>左侧导航收藏：按点击顺序记住模块 Key，再点一次取消。</summary>
    public static class NavFavoriteHelper
    {
        public static bool Contains(string[] keys, string key)
        {
            if (keys == null || string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < keys.Length; i++)
            {
                if (string.Equals(keys[i], key, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>已收藏则移除，否则追加到末尾。空 Key 原样返回副本。</summary>
        public static string[] Toggle(string[] keys, string key)
        {
            var list = Copy(keys);
            if (string.IsNullOrEmpty(key)) return list.ToArray();
            int at = IndexOf(list, key);
            if (at >= 0) list.RemoveAt(at);
            else list.Add(key);
            return list.ToArray();
        }

        public static string[] Normalize(string[] keys)
        {
            return Copy(keys).ToArray();
        }

        private static List<string> Copy(string[] keys)
        {
            var list = new List<string>();
            if (keys == null) return list;
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                if (string.IsNullOrEmpty(k)) continue;
                if (IndexOf(list, k) >= 0) continue;
                list.Add(k);
            }
            return list;
        }

        private static int IndexOf(List<string> list, string key)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], key, StringComparison.Ordinal)) return i;
            }
            return -1;
        }
    }
}
