using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>
    /// 左侧导航分类与工具顺序：默认按日常使用频率，设置里可上移下移覆盖。
    /// 设置模块不参与排序，始终钉在导航底部。
    /// </summary>
    public static class NavOrder
    {
        public static readonly string[] DefaultCategories =
        {
            "效率工具",
            "系统工具",
            "格式化",
            "截图",
            "文本工具",
            "编码转换",
            "转换工具",
            "文件管理",
            "网络工具",
            "安全工具",
            "生成器",
            "数据转换",
            "图片工具",
            "设计工具",
            "开发工具",
            "其他工具"
        };

        public static readonly string[] DefaultToolKeys =
        {
            "clipboard", "launcher", "todo", "notes", "vault",
            "machineinfo", "hostseditor", "filelock", "wintopmost", "keepawake", "regbrowse",
            "formatter",
            "screenshot", "screenruler",
            "diff", "regextest", "lineprocess", "namecase", "mdpreview", "textstats", "regexviz",
            "base64", "codec", "urlparse", "unicodeinspect",
            "timestamp",
            "filesearch", "fileorganize", "batchrename", "dupfiles",
            "portlist", "porttest",
            "hash", "crypto", "certdecode",
            "idgenerator", "qrcode", "jsontoclass",
            "txt2excel",
            "imageconvert",
            "colorpicker", "colorblind",
            "jsonschema", "semver",
            "demo"
        };

        public static List<ModuleInfo> Sort(IList<ModuleInfo> source)
        {
            return SortWith(source, EffectiveCategories(), EffectiveToolKeys());
        }

        public static List<ModuleInfo> SortDefault(IList<ModuleInfo> source)
        {
            return SortWith(source, DefaultCategories, DefaultToolKeys);
        }

        public static string[] EffectiveCategories()
        {
            return MergeNames(SavedCategories(), DefaultCategories, RegistryCategories());
        }

        public static string[] EffectiveToolKeys()
        {
            return MergeNames(SavedToolKeys(), DefaultToolKeys, RegistryToolKeys());
        }

        public static bool SameOrder(string[] a, string[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        public static bool IsDefaultOrder(string[] categories, string[] toolKeys)
        {
            return SameOrder(categories, DefaultCategories) && SameOrder(toolKeys, DefaultToolKeys);
        }

        private static List<ModuleInfo> SortWith(IList<ModuleInfo> source, string[] cats, string[] tools)
        {
            var list = new List<ModuleInfo>();
            if (source == null) return list;
            var orig = new Dictionary<string, int>();
            for (int i = 0; i < source.Count; i++)
            {
                var m = source[i];
                if (m == null || ToolVisibility.IsAlwaysPinned(m.Key)) continue;
                list.Add(m);
                if (!orig.ContainsKey(m.Key))
                    orig[m.Key] = i;
            }
            list.Sort(delegate(ModuleInfo a, ModuleInfo b)
            {
                int ca = Rank(cats, a.Category, 5000);
                int cb = Rank(cats, b.Category, 5000);
                if (ca != cb) return ca.CompareTo(cb);
                int ta = Rank(tools, a.Key, 10000);
                int tb = Rank(tools, b.Key, 10000);
                if (ta != tb) return ta.CompareTo(tb);
                int oa = orig.ContainsKey(a.Key) ? orig[a.Key] : 0;
                int ob = orig.ContainsKey(b.Key) ? orig[b.Key] : 0;
                return oa.CompareTo(ob);
            });
            return list;
        }

        private static int Rank(string[] order, string name, int missingBase)
        {
            int i = IndexOf(order, name);
            return i >= 0 ? i : missingBase + 1;
        }

        private static int IndexOf(string[] order, string name)
        {
            if (order == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == name) return i;
            }
            return -1;
        }

        private static string[] MergeNames(string[] saved, string[] defaults, string[] extras)
        {
            var list = new List<string>();
            AddUnique(list, saved);
            AddUnique(list, defaults);
            AddUnique(list, extras);
            return list.ToArray();
        }

        private static void AddUnique(List<string> list, string[] names)
        {
            if (names == null) return;
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                if (n == "系统") continue;
                bool found = false;
                for (int j = 0; j < list.Count; j++)
                {
                    if (list[j] == n)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) list.Add(n);
            }
        }

        private static string[] SavedCategories()
        {
            try
            {
                var keys = ConfigManager.Instance.Settings.NavCategoryOrder;
                return HasItems(keys) ? keys : null;
            }
            catch
            {
                return null;
            }
        }

        private static string[] SavedToolKeys()
        {
            try
            {
                var keys = ConfigManager.Instance.Settings.NavToolOrder;
                return HasItems(keys) ? keys : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool HasItems(string[] keys)
        {
            return keys != null && keys.Length > 0;
        }

        private static string[] RegistryCategories()
        {
            var list = new List<string>();
            var all = ModuleRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (ToolVisibility.IsAlwaysPinned(all[i].Key)) continue;
                AddUnique(list, new[] { all[i].Category });
            }
            return list.ToArray();
        }

        private static string[] RegistryToolKeys()
        {
            var list = new List<string>();
            var all = ModuleRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (ToolVisibility.IsAlwaysPinned(all[i].Key)) continue;
                list.Add(all[i].Key);
            }
            return list.ToArray();
        }
    }
}
