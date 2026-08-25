namespace IntraBox.Core
{
    /// <summary>
    /// 模块视图生命周期钩子（可选实现）。
    /// 实现它的控件会在「激活」与「销毁」时收到回调，用于：
    ///   - 激活时从 HistoryManager 恢复输入
    ///   - 销毁时把当前输入写入 HistoryManager、释放大对象引用
    /// 剪贴板监听由全局 ClipboardMonitor 常驻，不随模块启停。
    /// </summary>
    public interface IModuleView
    {
        /// <summary>模块被激活（加载进工作区）时调用</summary>
        void OnActivated();

        /// <summary>模块被销毁（切换离开或程序退出）时调用</summary>
        void OnDeactivated();
    }
}
