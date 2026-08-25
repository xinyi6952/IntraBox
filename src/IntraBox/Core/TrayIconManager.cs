using System;
using System.Drawing;
using System.Windows;
using WF = System.Windows.Forms;

namespace IntraBox.Core
{
    /// <summary>
    /// 系统托盘：单击打开主窗口；右键「显示主窗口 / 退出」。
    /// 点关闭只隐藏到托盘；从托盘退出才真正结束进程。
    /// </summary>
    public sealed class TrayIconManager : IDisposable
    {
        private WF.NotifyIcon _notifyIcon;

        public void Initialize()
        {
            _notifyIcon = new WF.NotifyIcon
            {
                Icon = ExtractAppIcon(),
                Text = "IntraBox",
                Visible = true
            };

            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (s, e) => ShowMainWindow());
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = menu;

            // 左键单击/双击都打开主窗口；右键仍弹出菜单
            _notifyIcon.MouseClick += NotifyIcon_MouseClick;
            _notifyIcon.MouseUp += NotifyIcon_MouseUp;
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        /// <summary>退出前确认（未保存等）。返回 false 则取消退出。</summary>
        public System.Func<bool> ConfirmExit { get; set; }

        private void NotifyIcon_MouseClick(object sender, WF.MouseEventArgs e)
        {
            if (e.Button == WF.MouseButtons.Left)
                ShowMainWindow();
        }

        private void NotifyIcon_MouseUp(object sender, WF.MouseEventArgs e)
        {
            if (e.Button == WF.MouseButtons.Left)
                ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            WindowRestore.ShowAndRestore(w);
        }

        private void ExitApplication()
        {
            ShowMainWindow();
            if (ConfirmExit != null && !ConfirmExit()) return;
            if (!ConfirmHelper.ExitApp()) return;
            ConfirmHelper.IsExiting = true;
            if (Application.Current != null) Application.Current.Shutdown();
        }

        private static Icon ExtractAppIcon()
        {
            try
            {
                using (var p = System.Diagnostics.Process.GetCurrentProcess())
                {
                    var path = p.MainModule.FileName;
                    return Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application;
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.MouseClick -= NotifyIcon_MouseClick;
                _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
