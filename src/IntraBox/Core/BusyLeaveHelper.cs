namespace IntraBox.Core
{
    /// <summary>切换工具前：在途任务二次确认文案与弹窗。</summary>
    public static class BusyLeaveHelper
    {
        public const string DefaultTitle = "任务未完成";

        public const string DefaultMessage =
            "当前工具仍有未完成的任务，离开将中止该任务。确定离开？";

        /// <summary>空或空白时用默认正文。</summary>
        public static string ResolveMessage(string busyMessage)
        {
            if (string.IsNullOrWhiteSpace(busyMessage)) return DefaultMessage;
            return busyMessage.Trim();
        }

        /// <summary>未在忙则 true；在忙则弹窗，是 → true，否 → false。</summary>
        public static bool ConfirmIfBusy(bool isBusy, string busyMessage)
        {
            if (!isBusy) return true;
            return ConfirmHelper.Warn(ResolveMessage(busyMessage), DefaultTitle);
        }
    }
}
