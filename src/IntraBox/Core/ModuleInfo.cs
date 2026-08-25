using System;
using System.Windows;

namespace IntraBox.Core
{
    /// <summary>
    /// 模块描述信息：用于导航展示，并持有「按需实例化」的工厂委托。
    /// 工厂只在用户真正进入该模块时才被调用，从而实现按需加载。
    /// </summary>
    public sealed class ModuleInfo
    {
        /// <summary>模块唯一标识（用于历史记录、缓存、清空等）</summary>
        public string Key { get; set; }

        /// <summary>导航中显示的名称</summary>
        public string DisplayName { get; set; }

        /// <summary>功能分类（用于导航分组，如「格式化」「效率工具」）</summary>
        public string Category { get; set; }

        /// <summary>折叠导航时显示的单字缩写。</summary>
        public string NavShort
        {
            get
            {
                if (string.IsNullOrEmpty(DisplayName)) return "?";
                return DisplayName.Substring(0, 1);
            }
        }

        /// <summary>模块控件工厂：延迟调用，返回该模块的界面根控件</summary>
        public Func<UIElement> Factory { get; set; }
    }
}
