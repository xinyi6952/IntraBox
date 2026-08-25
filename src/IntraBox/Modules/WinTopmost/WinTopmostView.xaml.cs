using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.WinTopmost
{
    public partial class WinTopmostView : UserControl, IModuleView
    {
        public WinTopmostView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            Refresh_Click(null, null);
        }

        public void OnDeactivated() { }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            var list = Win32Native.ListVisibleWindows();
            int self = Process.GetCurrentProcess().Id;
            if (SelfCheck.IsChecked != true)
            {
                list.RemoveAll(w => w.Pid == self);
            }
            WinList.ItemsSource = list;
            SetMsg("共 " + list.Count + " 个窗口", false);
        }

        private void Top_Click(object sender, RoutedEventArgs e)
        {
            SetTop(true);
        }

        private void Untop_Click(object sender, RoutedEventArgs e)
        {
            SetTop(false);
        }

        private void SetTop(bool top)
        {
            var item = WinList.SelectedItem as WindowEntry;
            if (item == null)
            {
                SetMsg("请先选中窗口", true);
                return;
            }
            IntPtr insert = new IntPtr(top ? Win32Native.HwndTopmost : Win32Native.HwndNotopmost);
            bool ok = Win32Native.SetWindowPos(item.Handle, insert, 0, 0, 0, 0, Win32Native.SwpNomove | Win32Native.SwpNosize);
            SetMsg(ok ? (top ? "已置顶" : "已取消置顶") : "操作失败", !ok);
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }
    }
}
