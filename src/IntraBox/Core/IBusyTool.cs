namespace IntraBox.Core
{
    /// <summary>
    /// 模块有耗时在途任务时实现。离开前二次确认；确认后仍走销毁路径取消任务。
    /// 未实现时，若当前视图挂着 LoadingOverlay 也会走同一确认（见 ModuleLoader）。
    /// </summary>
    public interface IBusyTool
    {
        /// <summary>是否仍有未完成的后台/遮罩任务。</summary>
        bool IsBusy { get; }

        /// <summary>确认框正文；空则用默认文案。</summary>
        string BusyMessage { get; }
    }
}
