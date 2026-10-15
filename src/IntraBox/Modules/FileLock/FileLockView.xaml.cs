using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
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
            FilterBar.Attach(ProcGrid);
        }

        public void OnActivated() { }
        public void OnDeactivated() { }

        private void Pick_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "选择要查看占用的文件", Filter = "所有文件|*.*" };
            if (dlg.ShowDialog() != true) return;
            _path = dlg.FileName;
            PathText.Text = _path;
            RefreshAsync();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshAsync();
        }

        private async void RefreshAsync()
        {
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
            {
                SetMsg("请先选择文件", true);
                return;
            }
            string path = _path;
            LoadingOverlay.Show(this, "正在查询哪个进程占用…");
            List<LockingProcess> list = null;
            string err = null;
            bool listed = false;
            try
            {
                await Task.Run(() =>
                {
                    listed = Win32Native.TryGetLockingProcesses(path, out list, out err);
                    if (!listed || list == null || list.Count == 0)
                        TryExclusive(path, out list);
                });
            }
            catch (Exception ex)
            {
                err = ex.RootMessage();
            }
            finally
            {
                LoadingOverlay.Hide(this);
            }
            if (!IsLoaded) return;
            if (list == null) list = new List<LockingProcess>();
            ProcGrid.ItemsSource = list;
            FilterBar.Apply();
            if (listed)
                SetMsg(list.Count == 0 ? "当前没有进程占用该文件" : "找到 " + list.Count + " 个占用进程", false);
            else
                SetMsg((err ?? "Restart Manager 不可用") + (list.Count == 0 ? "；独占打开成功，可能未被占用" : ""), list.Count > 0);
        }

        private static void TryExclusive(string path, out List<LockingProcess> list)
        {
            list = new List<LockingProcess>();
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
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
                list.Add(new LockingProcess { Pid = 0, Name = ex.RootMessage() });
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
                RefreshAsync();
            }
            catch (Exception ex)
            {
                SetMsg("结束失败：" + ex.RootMessage() + "（可能需要管理员权限）", true);
            }
        }

        private void SetMsg(string t, bool err)
        {
             FormHint.Apply(MsgText, t, err);
        }
    }
}
