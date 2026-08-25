using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.FileLock
{
    public partial class FileLockView : UserControl, IModuleView
    {
        private string _path;

        public FileLockView()
        {
            InitializeComponent();
        }

        public void OnActivated() { }
        public void OnDeactivated() { }

        private void Pick_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "选择要查看占用的文件", Filter = "所有文件|*.*" };
            if (dlg.ShowDialog() != true) return;
            _path = dlg.FileName;
            PathText.Text = _path;
            Refresh();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
            {
                SetMsg("请先选择文件", true);
                return;
            }
            List<LockingProcess> list;
            string err;
            if (Win32Native.TryGetLockingProcesses(_path, out list, out err))
            {
                if (list.Count == 0)
                    TryExclusive(out list);
                ProcGrid.ItemsSource = list;
                SetMsg(list.Count == 0 ? "当前没有进程占用该文件" : "找到 " + list.Count + " 个占用进程", false);
            }
            else
            {
                TryExclusive(out list);
                ProcGrid.ItemsSource = list;
                SetMsg((err ?? "Restart Manager 不可用") + (list.Count == 0 ? "；独占打开成功，可能未被占用" : ""), list.Count > 0);
            }
        }

        private void TryExclusive(out List<LockingProcess> list)
        {
            list = new List<LockingProcess>();
            try
            {
                using (File.Open(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            catch (IOException)
            {
                list.Add(new LockingProcess { Pid = 0, Name = "文件被占用（无法列出具体进程）" });
            }
            catch (UnauthorizedAccessException)
            {
                list.Add(new LockingProcess { Pid = 0, Name = "无权限读取该文件" });
            }
            catch (Exception ex)
            {
                list.Add(new LockingProcess { Pid = 0, Name = ex.Message });
            }
        }

        private void Kill_Click(object sender, RoutedEventArgs e)
        {
            var row = ProcGrid.SelectedItem as LockingProcess;
            if (row == null || row.Pid <= 4)
            {
                SetMsg("请选择可结束的进程", true);
                return;
            }
            if (MessageBox.Show("确定结束进程 " + row.Name + " (PID " + row.Pid + ")？", "结束进程",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                Process.GetProcessById(row.Pid).Kill();
                SetMsg("已结束 PID " + row.Pid, false);
                Refresh();
            }
            catch (Exception ex)
            {
                SetMsg("结束失败：" + ex.Message + "（可能需要管理员权限）", true);
            }
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }
    }
}
