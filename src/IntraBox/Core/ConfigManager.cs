using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace IntraBox.Core
{
    /// <summary>
    /// 配置管理器：负责配置的加载与保存（本地 JSON，程序目录存储）。
    /// 单例，供全局访问。容错策略：配置损坏时回退默认值，不让程序崩溃。
    /// </summary>
    public sealed class ConfigManager
    {
        private static readonly Lazy<ConfigManager> _instance =
            new Lazy<ConfigManager>(() => new ConfigManager());

        public static ConfigManager Instance => _instance.Value;

        /// <summary>当前配置（默认值，加载成功后覆盖）</summary>
        public AppSettings Settings { get; private set; } = new AppSettings();

        /// <summary>配置文件路径：程序目录下 config.json</summary>
        private static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;

                using (var fs = File.OpenRead(ConfigPath))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppSettings));
                    Settings = (AppSettings)ser.ReadObject(fs) ?? new AppSettings();
                    if (string.IsNullOrEmpty(Settings.Theme)) Settings.Theme = "Dark";
                    Settings.MaxFileSizeMb = SizeLimits.ClampMb(Settings.MaxFileSizeMb);
                    Settings.ClipboardMaxItems = AppSettings.ClampClipboardMax(Settings.ClipboardMaxItems);
                    Settings.HistoryPersistDelayMs = AppSettings.ClampHistoryPersistDelayMs(Settings.HistoryPersistDelayMs);
                }
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
            if (changed) Save();
        }
    }
}
