using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

        private void Load()
        {
            string text = "";
            try
            {
                if (File.Exists(_logPath))
                    text = File.ReadAllText(_logPath, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                FormHint.Apply(HintText, "读取失败：" + ex.RootMessage());
                return;
            }
            _entries = JobRunLogHelper.Parse(text);
            RunList.ItemsSource = _entries;
            FormHint.Apply(HintText, _entries.Count == 0
                ? "尚无执行记录"
                : ("共 " + _entries.Count + " 次"));
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
