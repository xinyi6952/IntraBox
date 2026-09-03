using System;

namespace IntraBox.Core
{
    /// <summary>异常提示：面向用户展示时取最内层真正的原因，避免 TargetInvocationException 等包装异常吞掉真因。</summary>
    public static class ExceptionHelper
    {
        /// <summary>返回异常链最底层（GetBaseException）的 Message，用于界面提示。</summary>
        public static string RootMessage(this Exception ex)
        {
            if (ex == null) return "";
            var baseEx = ex.GetBaseException();
            return baseEx != null ? baseEx.Message : ex.Message;
        }
    }
}
