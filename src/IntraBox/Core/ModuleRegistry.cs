using System;
using System.Collections.Generic;
using System.Windows;
using IntraBox.Modules.Base64;
using IntraBox.Modules.ClipboardHistory;
using IntraBox.Modules.Codec;
using IntraBox.Modules.ColorPicker;
using IntraBox.Modules.Crypto;
using IntraBox.Modules.Demo;
using IntraBox.Modules.Diff;
using IntraBox.Modules.FileOrganize;
using IntraBox.Modules.FileSearch;
using IntraBox.Modules.Formatter;
using IntraBox.Modules.Generator;
using IntraBox.Modules.Hash;
using IntraBox.Modules.JsonToClass;
using IntraBox.Modules.MachineInfo;
using IntraBox.Modules.NameCase;
using IntraBox.Modules.PortList;
using IntraBox.Modules.PortTest;
using IntraBox.Modules.QrCode;
using IntraBox.Modules.UrlParse;
using IntraBox.Modules.LineProcess;
using IntraBox.Modules.TextStats;
using IntraBox.Modules.BatchRename;
using IntraBox.Modules.FileLock;
using IntraBox.Modules.HostsEditor;
using IntraBox.Modules.WinTopmost;
using IntraBox.Modules.KeepAwake;
using IntraBox.Modules.CertDecode;
using IntraBox.Modules.UnicodeInspect;
using IntraBox.Modules.Semver;
using IntraBox.Modules.DupFiles;
using IntraBox.Modules.ImageConvert;
using IntraBox.Modules.MdPreview;
using IntraBox.Modules.RegexViz;
using IntraBox.Modules.RegBrowse;
using IntraBox.Modules.JsonSchema;
using IntraBox.Modules.ColorBlind;
using IntraBox.Modules.ScreenRuler;
using IntraBox.Modules.RegexTest;
using IntraBox.Modules.Screenshot;
using IntraBox.Modules.Settings;
using IntraBox.Modules.Timestamp;
using IntraBox.Modules.TxtToExcel;

namespace IntraBox.Core
{
    /// <summary>
    /// 模块注册表：集中登记所有功能模块，导航与加载都从这里读取。
    /// 新增模块时，在 RegisterDefault() 中加一行 Register(...) 即可。
    /// </summary>
    public static class ModuleRegistry
    {
        private static readonly List<ModuleInfo> _modules = new List<ModuleInfo>();

        /// <summary>全部已注册模块（只读）</summary>
        public static IReadOnlyList<ModuleInfo> All => _modules;

        static ModuleRegistry()
        {
            RegisterDefault();
        }

        /// <summary>
        /// 默认注册列表：集中登记全部功能模块。
        /// </summary>
        private static void RegisterDefault()
        {
            // 安全工具
            Register("hash", "哈希计算", "安全工具", () => new HashView());
            Register("crypto", "加解密", "安全工具", () => new CryptoView());
            Register("certdecode", "证书解析", "安全工具", () => new CertDecodeView());

            // 编码转换
            Register("base64", "Base64 编解码", "编码转换", () => new Base64View());
            Register("codec", "URL/HTML/Unicode 编解码", "编码转换", () => new CodecView());
            Register("urlparse", "URL 解析", "编码转换", () => new UrlParseView());
            Register("unicodeinspect", "Unicode 字符检查", "编码转换", () => new UnicodeInspectView());

            // 格式化
            Register("formatter", "格式化", "格式化", () => new FormatterView());

            // 截图
            Register("screenshot", "屏幕截图", "截图", () => new ScreenshotView());
            Register("screenruler", "屏幕标尺", "截图", () => new ScreenRulerView());

            // 开发工具
            Register("jsonschema", "JSON Schema 验证", "开发工具", () => new JsonSchemaView());
            Register("semver", "SemVer 版本比较", "开发工具", () => new SemverView());

            // 设计工具
            Register("colorblind", "色盲模拟", "设计工具", () => new ColorBlindView());
            Register("colorpicker", "屏幕取色", "设计工具", () => new ColorPickerView());

            // 生成器
            Register("qrcode", "二维码生成", "生成器", () => new QrCodeView());
            Register("jsontoclass", "JSON 转实体类", "生成器", () => new JsonToClassView());
            Register("idgenerator", "ID 与密码生成", "生成器", () => new GeneratorView());

            // 数据转换
            Register("txt2excel", "文本转 Excel", "数据转换", () => new TxtToExcelView());

            // 图片工具
            Register("imageconvert", "图片压缩缩放转换", "图片工具", () => new ImageConvertView());

            // 网络工具
            Register("portlist", "端口占用查看", "网络工具", () => new PortListView());
            Register("porttest", "端口连通性测试", "网络工具", () => new PortTestView());

            // 文本工具
            Register("lineprocess", "行处理", "文本工具", () => new LineProcessView());
            Register("mdpreview", "Markdown 预览", "文本工具", () => new MdPreviewView());
            Register("namecase", "命名转换", "文本工具", () => new NameCaseView());
            Register("diff", "文本比对", "文本工具", () => new DiffView());
            Register("textstats", "文本统计", "文本工具", () => new TextStatsView());
            Register("regextest", "正则表达式测试", "文本工具", () => new RegexTestView());
            Register("regexviz", "正则可视化", "文本工具", () => new RegexVizView());

            // 文件管理
            Register("dupfiles", "重复文件查找", "文件管理", () => new DupFilesView());
            Register("batchrename", "批量重命名", "文件管理", () => new BatchRenameView());
            Register("filesearch", "文件查找", "文件管理", () => new FileSearchView());
            Register("fileorganize", "文件整理", "文件管理", () => new FileOrganizeView());

            // 系统工具
            Register("machineinfo", "本机信息", "系统工具", () => new MachineInfoView());
            Register("wintopmost", "窗口置顶", "系统工具", () => new WinTopmostView());
            Register("keepawake", "防止睡眠", "系统工具", () => new KeepAwakeView());
            Register("hostseditor", "Hosts 编辑器", "系统工具", () => new HostsEditorView());
            Register("filelock", "文件占用查看", "系统工具", () => new FileLockView());
            Register("regbrowse", "注册表只读浏览", "系统工具", () => new RegBrowseView());

            // 效率工具
            Register("clipboard", "剪贴板历史", "效率工具", () => new ClipboardView());

            // 转换工具
            Register("timestamp", "时间戳转换", "转换工具", () => new TimestampView());

            // 设置（固定导航底部）
            Register("settings", "设置", "系统", () => new SettingsView());

            // 示例模块：演示「按需加载、用完销毁」机制
            Register("demo", "示例：按需加载", "其他工具", () => new DemoModule());
        }

        /// <summary>注册一个模块到注册表。</summary>
        private static void Register(string key, string displayName, string category, Func<UIElement> factory)
        {
            _modules.Add(new ModuleInfo
            {
                Key = key,
                DisplayName = displayName,
                Category = category,
                Factory = factory
            });
        }
    }
}
