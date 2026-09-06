using System;
using System.Runtime.Serialization;

namespace IntraBox.Core
{
    /// <summary>
    /// 应用配置模型：以本地 JSON 文件（数据根 config.json）持久化。
    /// 绿色免安装：配置、历史均随数据根存储，不写系统注册表。
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        /// <summary>配置文件版本，缺省按 1 补齐，便于后续字段兼容。</summary>
        [DataMember]
        public int ConfigVersion { get; set; } = DataPaths.CurrentConfigVersion;

        /// <summary>是否首次运行（用于后续引导）</summary>
        [DataMember]
        public bool FirstRun { get; set; } = true;

        /// <summary>缓存最近 N 个模块（0 = 用完立即销毁，最省内存）</summary>
        [DataMember]
        public int CacheModuleCount { get; set; } = 0;

        /// <summary>上次使用的模块标识（启动时可自动恢复）</summary>
        [DataMember]
        public string LastModuleKey { get; set; } = "";

        /// <summary>界面主题：Light / Dark / System（跟随系统）</summary>
        [DataMember]
        public string Theme { get; set; } = "Dark";

        /// <summary>左侧导航是否折叠</summary>
        [DataMember]
        public bool NavCollapsed { get; set; }

        /// <summary>打开/导入文件的大小上限（MB）。默认 10，硬顶 50。</summary>
        [DataMember]
        public int MaxFileSizeMb { get; set; } = 10;

        /// <summary>截屏热键 Ctrl。</summary>
        [DataMember]
        public bool CaptureHotkeyCtrl { get; set; } = true;

        /// <summary>截屏热键 Alt。</summary>
        [DataMember]
        public bool CaptureHotkeyAlt { get; set; } = true;

        /// <summary>截屏热键 Shift。</summary>
        [DataMember]
        public bool CaptureHotkeyShift { get; set; } = false;

        /// <summary>截屏热键虚拟键码，默认 A=0x41。</summary>
        [DataMember]
        public int CaptureHotkeyVk { get; set; } = 0x41;

        /// <summary>
        /// 左侧导航可见工具的模块 Key。null 或空 = 全部显示。
        /// 设置不在此列表中，始终固定在导航底部。
        /// </summary>
        [DataMember]
        public string[] VisibleToolKeys { get; set; }

        /// <summary>左侧导航分类顺序。null 或空 = 使用默认（按使用频率）。</summary>
        [DataMember]
        public string[] NavCategoryOrder { get; set; }

        /// <summary>左侧导航工具 Key 顺序。null 或空 = 使用默认。工具仍留在原分类内。</summary>
        [DataMember]
        public string[] NavToolOrder { get; set; }

        /// <summary>上次关闭/藏到托盘前主窗口是否最大化。</summary>
        [DataMember]
        public bool WindowMaximized { get; set; }

        /// <summary>剪贴板历史条数。默认 20，范围 1–200。</summary>
        [DataMember]
        public int ClipboardMaxItems { get; set; } = 20;

        /// <summary>是否把剪贴板中的图片记入历史。默认 false；开关在剪贴板工具顶栏。</summary>
        [DataMember]
        public bool ClipboardRecordImages { get; set; }

        /// <summary>将条数夹取到 1–200（小于 1 变为 1，大于 200 变为 200）。</summary>
        public static int ClampClipboardMax(int n)
        {
            return Math.Max(1, Math.Min(200, n));
        }

        public const int HistoryPersistDelayMinMs = 300;
        public const int HistoryPersistDelayMaxMs = 1000;

        /// <summary>所有工具状态记忆写入磁盘的延迟（毫秒）。默认 300，范围 300–1000。</summary>
        [DataMember]
        public int HistoryPersistDelayMs { get; set; } = HistoryPersistDelayMinMs;

        public static int ClampHistoryPersistDelayMs(int n)
        {
            return Math.Max(HistoryPersistDelayMinMs, Math.Min(HistoryPersistDelayMaxMs, n));
        }

        /// <summary>当前生效的落盘延迟；配置未加载或越界时回落到 300–1000。</summary>
        public static int CurrentHistoryPersistDelayMs()
        {
            try
            {
                return ClampHistoryPersistDelayMs(ConfigManager.Instance.Settings.HistoryPersistDelayMs);
            }
            catch
            {
                return HistoryPersistDelayMinMs;
            }
        }

        /// <summary>屏幕标尺热键 Ctrl。</summary>
        [DataMember]
        public bool RulerHotkeyCtrl { get; set; } = true;

        /// <summary>屏幕标尺热键 Alt。</summary>
        [DataMember]
        public bool RulerHotkeyAlt { get; set; } = false;

        /// <summary>屏幕标尺热键 Shift。</summary>
        [DataMember]
        public bool RulerHotkeyShift { get; set; } = true;

        /// <summary>屏幕标尺热键虚拟键，默认 M。</summary>
        [DataMember]
        public int RulerHotkeyVk { get; set; } = 0x4D;

        /// <summary>启动器热键 Ctrl。</summary>
        [DataMember]
        public bool LauncherHotkeyCtrl { get; set; } = true;

        /// <summary>启动器热键 Alt。</summary>
        [DataMember]
        public bool LauncherHotkeyAlt { get; set; } = true;

        /// <summary>启动器热键 Shift。</summary>
        [DataMember]
        public bool LauncherHotkeyShift { get; set; } = false;

        /// <summary>启动器热键虚拟键码，默认 L=0x4C。</summary>
        [DataMember]
        public int LauncherHotkeyVk { get; set; } = 0x4C;

        /// <summary>笔记是否自动保存。默认关闭，需点保存或 Ctrl+S。</summary>
        [DataMember]
        public bool NotesAutoSave { get; set; }

        /// <summary>账号备忘是否自动保存。默认关闭。</summary>
        [DataMember]
        public bool VaultAutoSave { get; set; }

        /// <summary>从账号备忘复制时是否写入剪贴板历史。默认 false（不记入）。</summary>
        [DataMember]
        public bool ClipboardRecordVaultCopies { get; set; }

        /// <summary>
        /// 点标题栏关闭/Alt+F4 时是否跳过「最小化到托盘 / 退出」询问。
        /// 为 true 时按 ClosePreferExit 直接执行；托盘右键「退出」不受此项影响。
        /// </summary>
        [DataMember]
        public bool ClosePromptSkip { get; set; }

        /// <summary>
        /// 关闭提示勾选「不再提醒」后记住的选择：true=退出进程，false=最小化到托盘。
        /// 默认 true，与首次弹窗预选「退出」一致。
        /// </summary>
        [DataMember]
        public bool ClosePreferExit { get; set; } = true;
    }
}
