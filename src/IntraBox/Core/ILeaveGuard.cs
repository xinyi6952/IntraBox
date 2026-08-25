namespace IntraBox.Core
{
    /// <summary>
    /// 离开模块前确认（如比对未保存）。返回 false 则取消切换或退出。
    /// </summary>
    public interface ILeaveGuard
    {
        bool CanLeave();
    }
}
