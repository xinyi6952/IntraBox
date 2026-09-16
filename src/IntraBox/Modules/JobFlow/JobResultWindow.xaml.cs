using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using IntraBox.Core;

namespace IntraBox.Modules.JobFlow
{
    public partial class JobResultWindow : Window
    {
        public JobResultWindow(string jobName, bool ok, string summary, string detail)
        {
            InitializeComponent();
            Title = "运行结果 — " + (string.IsNullOrEmpty(jobName) ? "编排" : jobName);
            FormHint.Apply(StatusText, ok ? "成功" : "失败", !ok);
            SummaryText.Text = summary ?? "";
            DetailBox.Text = detail ?? "";
        }

        public static void ShowResult(Window owner, string jobName, JobRunResult result)
        {
            if (result == null)
            {
                ShowException(owner, jobName, new InvalidOperationException("无运行结果"));
                return;
            }
            string summary = result.Ok
                ? (string.IsNullOrEmpty(result.Message) ? "运行成功" : result.Message)
                : (string.IsNullOrEmpty(result.Message) ? "运行失败" : result.Message);
            var win = new JobResultWindow(jobName, result.Ok, summary, FormatLines(result.Lines));
            win.Owner = owner;
            win.ShowDialog();
        }

        public static void ShowException(Window owner, string jobName, Exception ex)
        {
            string root = ex != null ? ex.RootMessage() : "未知错误";
            string detail = FormatException(ex);
            var win = new JobResultWindow(jobName, false, "运行异常：" + root, detail);
            win.Owner = owner;
            win.ShowDialog();
        }

        private static string FormatLines(IList<string> lines)
        {
            if (lines == null || lines.Count == 0) return "(无日志)";
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
                sb.AppendLine(lines[i] ?? "");
            return sb.ToString().TrimEnd();
        }

        private static string FormatException(Exception ex)
        {
            if (ex == null) return "(无异常信息)";
            var sb = new StringBuilder();
            sb.AppendLine(ex.RootMessage());
            sb.AppendLine();
            Exception cur = ex;
            int depth = 0;
            while (cur != null && depth < 8)
            {
                sb.AppendLine("--- " + cur.GetType().Name + " ---");
                sb.AppendLine(cur.Message ?? "");
                if (!string.IsNullOrEmpty(cur.StackTrace))
                    sb.AppendLine(cur.StackTrace);
                cur = cur.InnerException;
                depth++;
            }
            return sb.ToString().TrimEnd();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
