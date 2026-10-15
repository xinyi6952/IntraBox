using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.JobFlow
{
    public partial class JobHistoryWindow : Window
    {
        private readonly string _uid;
        private readonly string _logPath;
        private List<JobRunLogEntry> _entries = new List<JobRunLogEntry>();

        public JobHistoryWindow(string uid, string jobName)
        {
            InitializeComponent();
            _uid = uid ?? "";
            _logPath = JobFlowStore.RunLogPath(_uid);
            Title = "执行记录 — " + (string.IsNullOrEmpty(jobName) ? _uid : jobName);
            Load();
        }

        private async void Load()
        {
            bool heavy = false;
            try
            {
                heavy = File.Exists(_logPath) && new FileInfo(_logPath).Length > JobRunLogHelper.LogTailBytes;
            }
            catch (Exception ex)
            {
                FormHint.Apply(HintText, "读取失败：" + ex.Message, true);
                return;
            }
            JobLogRead read;
            if (heavy)
            {
                LoadingOverlay.Show(this, "正在读取运行日志…");
                try
                {
                    read = await Task.Run(() => JobRunLogHelper.ReadTail(_logPath));
                }
                finally
                {
                    LoadingOverlay.Hide(this);
                }
            }
            else
                read = JobRunLogHelper.ReadTail(_logPath);
            if (!string.IsNullOrEmpty(read.Error))
            {
                FormHint.Apply(HintText, "读取失败：" + read.Error, true);
                return;
            }
            string text = read.Text ?? "";
            _entries = JobRunLogHelper.Parse(text);
            RunList.ItemsSource = _entries;
            string hint = _entries.Count == 0
                ? "尚无执行记录"
                : ("共 " + _entries.Count + " 次");
            if (read.TruncatedFile)
                hint += "。日志过长，仅读取末尾约 256 KB";
            FormHint.Apply(HintText, hint);
            if (_entries.Count > 0)
                RunList.SelectedIndex = 0;
            else
                DetailBox.Text = "";
        }

        private void RunList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var entry = RunList.SelectedItem as JobRunLogEntry;
            if (entry == null)
            {
                DetailBox.Text = "";
                return;
            }
            DetailBox.Text = entry.StatusText + Environment.NewLine + Environment.NewLine + (entry.Body ?? "");
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = JobFlowStore.JobDir(_uid);
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.RootMessage(), "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(_logPath))
                {
                    MessageBox.Show("日志文件尚不存在。", "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                Process.Start(new ProcessStartInfo { FileName = _logPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.RootMessage(), "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
