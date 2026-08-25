using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IntraBox.Core;
using WinForms = System.Windows.Forms;

namespace IntraBox.Modules.FileSearch
{
    /// <summary>
    /// 目录内按文件名关键字/通配符搜索，可按大小、修改时间过滤。
    /// 参考 SearchFast / FolderDeepSearch：EnumerateFiles + 通配符转正则，结果可定位到资源管理器。
    /// </summary>
    public partial class FileSearchView : UserControl, IModuleView
    {
        private readonly List<Hit> _hits = new List<Hit>();

        public FileSearchView()
        {
            InitializeComponent();
            ResultGrid.ItemsSource = _hits;
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (HistoryManager.TryLoad("filesearch", out state))
            {
                DirBox.Text = HistoryManager.GetString(state, "dir");
                KeywordBox.Text = HistoryManager.GetString(state, "kw");
                SubDirCheck.IsChecked = HistoryManager.GetBool(state, "sub", true);
            }
            if (string.IsNullOrWhiteSpace(DirBox.Text))
                DirBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            MsgText.Text = "输入关键字或通配符后搜索。双击或点「定位到文件夹」打开资源管理器。";
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("filesearch", new Dictionary<string, object>
            {
                { "dir", DirBox.Text ?? "" },
                { "kw", KeywordBox.Text ?? "" },
                { "sub", SubDirCheck.IsChecked == true }
            });
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择搜索目录";
                if (!string.IsNullOrWhiteSpace(DirBox.Text) && Directory.Exists(DirBox.Text))
                    dlg.SelectedPath = DirBox.Text;
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                    DirBox.Text = dlg.SelectedPath;
            }
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            _hits.Clear();
            string dir = (DirBox.Text ?? "").Trim();
            if (!Directory.Exists(dir))
            {
                MsgText.Text = "目录不存在。";
                ResultGrid.Items.Refresh();
                return;
            }

            string kw = KeywordBox.Text ?? "";
            Regex nameRx = ToNameRegex(kw);
            long minB = ParseMb(MinSizeBox.Text, 0);
            long maxB = ParseMb(MaxSizeBox.Text, long.MaxValue);
            DateTime from = FromDate.SelectedDate ?? DateTime.MinValue;
            DateTime to = ToDate.SelectedDate.HasValue
                ? ToDate.SelectedDate.Value.Date.AddDays(1)
                : DateTime.MaxValue;
            var option = SubDirCheck.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            int skipped = 0;
            try
            {
                foreach (var path in Directory.EnumerateFiles(dir, "*", option))
                {
                    if (_hits.Count >= 2000)
                    {
                        MsgText.Text = "结果超过 2000 条，已截断。请缩小范围。";
                        ResultGrid.Items.Refresh();
                        return;
                    }
                    string name = Path.GetFileName(path);
                    if (nameRx != null && !nameRx.IsMatch(name)) continue;
                    FileInfo fi;
                    try { fi = new FileInfo(path); }
                    catch { skipped++; continue; }
                    if (fi.Length < minB || fi.Length > maxB) continue;
                    if (fi.LastWriteTime < from || fi.LastWriteTime >= to) continue;
                    _hits.Add(new Hit
                    {
                        Name = name,
                        Directory = fi.DirectoryName,
                        FullPath = path,
                        SizeText = SizeLimits.FormatBytes(fi.Length),
                        ModifiedText = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                    });
                }
            }
            catch (Exception ex)
            {
                MsgText.Text = "搜索失败：" + ex.Message;
                ResultGrid.Items.Refresh();
                return;
            }
            MsgText.Text = "找到 " + _hits.Count + " 个文件"
                + (skipped > 0 ? "，跳过 " + skipped + " 个无权限项" : "") + "。";
            ResultGrid.Items.Refresh();
        }

        private void Locate_Click(object sender, RoutedEventArgs e)
        {
            OpenSelected();
        }

        private void ResultGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelected();
        }

        private void OpenSelected()
        {
            var hit = ResultGrid.SelectedItem as Hit;
            if (hit == null || string.IsNullOrEmpty(hit.FullPath))
            {
                MsgText.Text = "请先选中一条结果。";
                return;
            }
            try
            {
                Process.Start("explorer.exe", "/select,\"" + hit.FullPath + "\"");
            }
            catch (Exception ex)
            {
                MsgText.Text = "无法打开资源管理器：" + ex.Message;
            }
        }

        private static Regex ToNameRegex(string kw)
        {
            kw = (kw ?? "").Trim();
            if (kw.Length == 0) return null;
            if (kw.IndexOf('*') < 0 && kw.IndexOf('?') < 0)
                return new Regex(Regex.Escape(kw), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            string pattern = "^" + Regex.Escape(kw).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static long ParseMb(string text, long fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            double mb;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out mb))
                if (!double.TryParse(text.Trim(), out mb)) return fallback;
            if (mb < 0) return fallback;
            return (long)(mb * 1024 * 1024);
        }

        private sealed class Hit
        {
            public string Name { get; set; }
            public string Directory { get; set; }
            public string FullPath { get; set; }
            public string SizeText { get; set; }
            public string ModifiedText { get; set; }
        }
    }
}
