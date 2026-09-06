using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>
    /// 左侧导航工具显隐：只过滤显示，不从 ModuleRegistry 删除模块。
    /// 设置始终固定在导航底部，不受勾选影响。
    /// </summary>
    public static class ToolVisibility
    {
        public const string SettingsKey = "settings";

        public static bool IsAlwaysPinned(string key)
        {
            return key == SettingsKey;
        }

        /// <summary>无配置或空列表视为全部可见。</summary>
        public static bool IsVisible(string key)
        {
            if (IsAlwaysPinned(key)) return true;
            var keys = ConfigManager.Instance.Settings.VisibleToolKeys;
            if (keys == null || keys.Length == 0) return true;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == key) return true;
            }
            return false;
        }

        public static ModuleInfo Find(string key)
        {
            var all = ModuleRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Key == key) return all[i];
            }
            return null;
        }

        /// <summary>可勾选的工具（不含设置），按当前导航顺序。</summary>
        public static List<ModuleInfo> ToggleableTools()
        {
            return NavOrder.Sort(ToggleableRaw());
        }

        /// <summary>默认导航顺序（忽略用户自定义）。</summary>
        public static List<ModuleInfo> ToggleableToolsDefault()
        {
            return NavOrder.SortDefault(ToggleableRaw());
        }

        /// <summary>左侧可滚动导航要显示的工具。</summary>
        public static List<ModuleInfo> NavTools()
        {
            var src = ToggleableTools();
            var list = new List<ModuleInfo>();
            for (int i = 0; i < src.Count; i++)
            {
                if (IsVisible(src[i].Key)) list.Add(src[i]);
            }
            return list;
        }

        private static List<ModuleInfo> ToggleableRaw()
        {
            var list = new List<ModuleInfo>();
            var all = ModuleRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!IsAlwaysPinned(all[i].Key))
                    list.Add(all[i]);
            }
            return list;
        }
    }
}
