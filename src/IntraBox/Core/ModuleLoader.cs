using System.Windows;
using System.Windows.Controls;

namespace IntraBox.Core
{
    /// <summary>
    /// 模块加载器：实现「按需加载、用完销毁」核心策略。
    /// 同一时刻工作区只挂载一个模块控件；切换时销毁旧的、按需创建新的。
    /// 默认「切换即销毁」最大化省内存；模块输入靠 HistoryManager 只存数据、不缓存控件。
    /// 模块仍按本加载器创建/销毁，不在此预创建控件。
    /// </summary>
    public sealed class ModuleLoader
    {
        private UIElement _current;      // 当前挂载的控件
        private ModuleInfo _currentInfo; // 当前模块描述
        private ContentControl _host;    // 工作区宿主，销毁时需摘掉 Content 以触发 Unloaded

        public ModuleInfo CurrentInfo { get { return _currentInfo; } }
        public UIElement CurrentView { get { return _current; } }

        /// <summary>
        /// 激活指定模块。若当前模块拒绝离开（未保存），返回 false 且不切换。
        /// </summary>
        public bool Activate(ModuleInfo info, ContentControl host)
        {
            if (info == null) return false;

            // 点击的是当前已激活模块，则不重复创建
            if (_currentInfo != null && _currentInfo.Key == info.Key) return true;

            if (!TryLeave()) return false;
            DestroyCurrent();

            // 按需实例化新模块控件
            var view = info.Factory();
            _host = host;
            host.Content = view;
            _current = view;
            _currentInfo = info;

            // 触发激活回调（从 HistoryManager 恢复输入、启动监听等）
            if (view is IModuleView m) m.OnActivated();
            return true;
        }

        /// <summary>退出程序前：确认离开并销毁当前模块。拒绝则返回 false。</summary>
        public bool TryDeactivate()
        {
            if (!TryLeave()) return false;
            DestroyCurrent();
            return true;
        }

        private bool TryLeave()
        {
            var guard = _current as ILeaveGuard;
            return guard == null || guard.CanLeave();
        }

        /// <summary>
        /// 销毁当前模块：触发释放回调并断开引用，交由 GC 回收。
        /// </summary>
        public void DestroyCurrent()
        {
            bool hadView = _current != null;

            if (_current is IModuleView m) m.OnDeactivated();
            if (_host != null) _host.Content = null;

            _current = null;
            _currentInfo = null;

            if (hadView) GcHelper.CollectSafely();
        }
    }
}
