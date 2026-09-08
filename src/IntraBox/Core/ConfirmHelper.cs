using System;
using System.Windows;

namespace IntraBox.Core
{
    /// <summary>删除、退出、未保存等二次确认。</summary>
    public static class ConfirmHelper
    {
        /// <summary>正在执行真正退出（托盘「退出」），主窗口 Closing 时不要再藏到托盘。</summary>
        public static bool IsExiting { get; set; }

        public static bool Delete(string detail)
        {
            string msg = string.IsNullOrEmpty(detail) ? "确定删除？" : "确定删除？\n\n" + detail;
            return MessageBox.Show(msg, "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                == MessageBoxResult.Yes;
        }

        /// <summary>置顶窗口上弹出删除确认，避免对话框被挡住。</summary>
        public static bool DeleteOverTopmost(Window owner, string detail)
        {
            return OverTopmost(owner, () => Delete(detail));
        }

        /// <summary>置顶窗口上的警告确认，默认选「否」防误触。</summary>
        public static bool WarnOverTopmost(Window owner, string message, string title)
        {
            return OverTopmost(owner, () =>
                MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                    == MessageBoxResult.Yes);
        }

        private static bool OverTopmost(Window owner, System.Func<bool> show)
        {
            bool top = false;
            if (owner != null)
            {
                top = owner.Topmost;
                owner.Topmost = false;
            }
            try
            {
                return show();
            }
            finally
            {
                if (owner != null) owner.Topmost = top;
            }
        }

        public static bool Action(string message, string title)
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                == MessageBoxResult.Yes;
        }

        /// <summary>格式错误等警告确认，默认选「否」。</summary>
        public static bool Warn(string message, string title)
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                == MessageBoxResult.Yes;
        }

        public static bool ExitApp()
        {
            return MessageBox.Show(
                "确定退出 IntraBox？退出后托盘图标也会关闭。",
                "退出确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        /// <summary>未保存：Yes 保存，No 不保存，Cancel 留下。</summary>
        public static MessageBoxResult Unsaved(string message, string title)
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        }
    }
}
