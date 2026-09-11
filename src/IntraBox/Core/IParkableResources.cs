namespace IntraBox.Core
{
    /// <summary>
    /// 藏托盘时暂释放大对象（IE 文档等），不销毁控件；再打开时恢复。
    /// 有未保存内容、不能静默卸载的工具走这条，避免托盘里仍占着预览引擎文档。
    /// </summary>
    public interface IParkableResources
    {
        void ParkHeavyResources();
        void UnparkHeavyResources();
    }
}
