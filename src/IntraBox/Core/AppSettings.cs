using System;
using System.Runtime.Serialization;

namespace IntraBox.Core
{
    /// <summary>
    /// 应用配置模型：以本地 JSON 文件（程序目录 config.json）持久化。
    /// 绿色免安装：配置、历史均随程序目录存储，不写系统注册表。
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
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

        /// <summary>上次关闭/藏到托盘前主窗口是否最大化。</summary>
        [DataMember]
        public bool WindowMaximized { get; set; }

        /// <summary>剪贴板历史条数。默认 20，范围 1–200。</summary>
        [DataMember]
        public int ClipboardMaxItems { get; set; } = 20;

        /// <summary>将条数夹取到 1–200（小于 1 变为 1，大于 200 变为 200）。</summary>
        public static int ClampClipboardMax(int n)
        {
            return Math.Max(1, Math.Min(200, n));
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
    }
}
