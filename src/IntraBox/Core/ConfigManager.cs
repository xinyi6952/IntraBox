using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace IntraBox.Core
{
    /// <summary>
    /// 配置管理器：负责配置的加载与保存（本地 JSON，数据根目录存储）。
    /// 单例，供全局访问。容错策略：配置损坏时回退默认值，不让程序崩溃。
    /// </summary>
    public sealed class ConfigManager
    {
        private static readonly Lazy<ConfigManager> _instance =
            new Lazy<ConfigManager>(() => new ConfigManager());

        public static ConfigManager Instance => _instance.Value;

        /// <summary>当前配置（默认值，加载成功后覆盖）</summary>
        public AppSettings Settings { get; private set; } = new AppSettings();

        /// <summary>配置文件路径：数据根目录下 config.json</summary>
        private static string ConfigPath
        {
            get { return DataPaths.ConfigJson; }
        }

        public void Load()
        {
            try
            {
                DataPaths.Initialize();
                if (!File.Exists(ConfigPath)) return;

                bool memChanged;
                using (var fs = File.OpenRead(ConfigPath))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppSettings));
                    Settings = (AppSettings)ser.ReadObject(fs) ?? new AppSettings();
                    if (string.IsNullOrEmpty(Settings.Theme)) Settings.Theme = "Dark";
                    if (Settings.ConfigVersion <= 0) Settings.ConfigVersion = 1;
                    Settings.MaxFileSizeMb = SizeLimits.ClampMb(Settings.MaxFileSizeMb);
                    Settings.ClipboardMaxItems = AppSettings.ClampClipboardMax(Settings.ClipboardMaxItems);
                    Settings.ClipboardMaxImageItems = AppSettings.ClampClipboardMaxImages(Settings.ClipboardMaxImageItems);
                    Settings.HistoryPersistDelayMs = AppSettings.ClampHistoryPersistDelayMs(Settings.HistoryPersistDelayMs);
                    memChanged = EnsureMemorySettingsLocked();
                }
                if (memChanged) Save();
            }
            catch (Exception)
            {
                // 配置损坏或读取失败时回退到默认值，不影响启动
                Settings = new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                Settings.ConfigVersion = DataPaths.CurrentConfigVersion;
                DataPaths.Initialize();
                using (var fs = File.Create(ConfigPath))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppSettings));
                    ser.WriteObject(fs, Settings);
                }
            }
            catch (Exception)
            {
                // 保存失败（如目录无写权限）时静默，不阻断运行
            }
        }

        /// <summary>
        /// 配置版本 2：补齐内存压缩开关的缺省（DataContract 缺 bool 为 false）。
        /// 水位/空闲秒数字段 0 按默认夹取。
        /// </summary>
        public void EnsureMemorySettings()
        {
            if (ApplyMemorySettings(Settings)) Save();
        }

        /// <summary>
        /// 配置版本 2：DataContract 缺 bool 为 false，不是字段初始值 true。
        /// 版本&lt;2 时补开三项默认；已是 2 则尊重用户关闭。水位/空闲秒 0 或越界按策略夹取。
        /// 不写盘，便于单测。
        /// </summary>
        public static bool ApplyMemorySettings(AppSettings s)
        {
            if (s == null) return false;
            bool changed = false;
            if (s.ConfigVersion < 2)
            {
                s.TrayIdleUnloadModule = true;
                s.ClipboardSkipImagesWhenHidden = true;
                s.RestoreLastModuleOnStartup = true;
                s.ConfigVersion = 2;
                changed = true;
            }
            int idle = MemoryTrimPolicy.ClampIdleSec(s.TrayIdleReleaseSec);
            int high = MemoryTrimPolicy.ClampHighMb(s.MemoryTrimHighMb);
            int low = MemoryTrimPolicy.ClampLowMb(s.MemoryTrimLowMb, high);
            if (s.TrayIdleReleaseSec != idle) { s.TrayIdleReleaseSec = idle; changed = true; }
            if (s.MemoryTrimHighMb != high) { s.MemoryTrimHighMb = high; changed = true; }
            if (s.MemoryTrimLowMb != low) { s.MemoryTrimLowMb = low; changed = true; }
            return changed;
        }

        private bool EnsureMemorySettingsLocked()
        {
            return ApplyMemorySettings(Settings);
        }

        /// <summary>
        /// 一次性迁移：旧版本把生成器模块的 key 记为 generator，现改为 idgenerator。
        /// 覆盖工具显隐（VisibleToolKeys）与上次工具（LastModuleKey），迁移后立即保存。
        /// </summary>
        public void MigrateGeneratorKey()
        {
            const string oldKey = "generator";
            const string newKey = "idgenerator";
            bool changed = false;

            var s = Settings;
            if (s.VisibleToolKeys != null)
            {
                for (int i = 0; i < s.VisibleToolKeys.Length; i++)
                {
                    if (s.VisibleToolKeys[i] == oldKey)
                    {
                        s.VisibleToolKeys[i] = newKey;
                        changed = true;
                    }
                }
            }
            if (s.LastModuleKey == oldKey)
            {
                s.LastModuleKey = newKey;
                changed = true;
            }
            if (s.NavToolOrder != null)
            {
                for (int i = 0; i < s.NavToolOrder.Length; i++)
                {
                    if (s.NavToolOrder[i] == oldKey)
                    {
                        s.NavToolOrder[i] = newKey;
                        changed = true;
                    }
                }
            }
            if (changed) Save();
        }

        /// <summary>
        /// 老用户已保存显隐白名单时，把新工具 key 追加进去（去重）。
        /// null 或空列表表示「全部可见」，原样返回，不改动。
        /// 追加顺序与 NavOrder.DefaultToolKeys 里效率工具一致：launcher、todo、notes、vault。
        /// </summary>
        public static string[] AppendNewVisibleToolKeys(string[] current)
        {
            if (current == null || current.Length == 0) return current;
            string[] extras = { "launcher", "todo", "notes", "vault" };
            var list = new System.Collections.Generic.List<string>(current);
            bool changed = false;
            for (int i = 0; i < extras.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < list.Count; j++)
                {
                    if (list[j] == extras[i]) { found = true; break; }
                }
                if (!found)
                {
                    list.Add(extras[i]);
                    changed = true;
                }
            }
            if (!changed) return current;
            return list.ToArray();
        }

        /// <summary>新模块默认加入已保存的显隐白名单，避免老用户看不到。</summary>
        public void EnsureNewToolsVisible()
        {
            var s = Settings;
            var next = AppendNewVisibleToolKeys(s.VisibleToolKeys);
            if (object.ReferenceEquals(next, s.VisibleToolKeys)) return;
            s.VisibleToolKeys = next;
            Save();
        }
    }
}
