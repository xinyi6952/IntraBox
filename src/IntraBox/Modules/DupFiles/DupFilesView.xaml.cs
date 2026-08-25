using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;
using WinForms = System.Windows.Forms;

namespace IntraBox.Modules.DupFiles
{
    public partial class DupFilesView : UserControl, IModuleView
    {
        private string _dir;
        private Thread _th;
        private volatile bool _cancel;

        public DupFilesView() { InitializeComponent(); }
        public void OnActivated() { }
        public void OnDeactivated() { _cancel = true; }

        private void Pick_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择要扫描的目录";
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                {
                    _dir = dlg.SelectedPath;
                    MsgText.Text = _dir;
                }
            }
        }

        private void Scan_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_dir) || !Directory.Exists(_dir))
            {
                MsgText.Text = "请先选择目录";
                return;
            }
            _cancel = true;
            if (_th != null && _th.IsAlive) _th.Join(200);
            _cancel = false;
            var dir = _dir;
            bool sub = SubCheck.IsChecked == true;
            _th = new Thread(() => Scan(dir, sub));
            _th.IsBackground = true;
            _th.Start();
        }

        private void Scan(string dir, bool sub)
        {
            try
            {
                var opt = sub ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                string[] files;
                try { files = Directory.GetFiles(dir, "*", opt); }
                catch (Exception ex)
                {
                    Ui(() => { MsgText.Text = ex.Message; });
                    return;
                }
                var bySize = new Dictionary<long, List<string>>();
                for (int i = 0; i < files.Length; i++)
                {
                    if (_cancel) return;
                    try
                    {
                        long len = new FileInfo(files[i]).Length;
                        List<string> list;
                        if (!bySize.TryGetValue(len, out list)) { list = new List<string>(); bySize[len] = list; }
                        list.Add(files[i]);
                    }
                    catch { }
                    if (i % 50 == 0)
                    {
                        int p = i;
                        Ui(() => { Bar.Value = files.Length == 0 ? 0 : (double)p * 50.0 / files.Length; MsgText.Text = "扫描 " + p + "/" + files.Length; });
                    }
                }
                var dups = new List<DupRow>();
                int g = 1;
                int done = 0;
                foreach (var kv in bySize)
                {
                    if (_cancel) return;
                    if (kv.Value.Count < 2) continue;
                    var hashes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in kv.Value)
                    {
                        if (_cancel) return;
                        string h;
                        try { h = HashFile(f); }
                        catch { continue; }
                        List<string> list;
                        if (!hashes.TryGetValue(h, out list)) { list = new List<string>(); hashes[h] = list; }
                        list.Add(f);
                    }
                    foreach (var hg in hashes)
                    {
                        if (hg.Value.Count < 2) continue;
                        foreach (var p in hg.Value)
                        {
                            dups.Add(new DupRow
                            {
                                Group = g,
                                Path = p,
                                SizeText = kv.Key.ToString(),
                                HashShort = hg.Key.Length > 16 ? hg.Key.Substring(0, 16) : hg.Key
                            });
                        }
                        g++;
                    }
                    done++;
                    int d = done;
                    Ui(() => { Bar.Value = 50 + (d % 50); });
                }
                Ui(() =>
                {
                    ResultList.ItemsSource = dups;
                    Bar.Value = 100;
                    MsgText.Text = "完成，重复组 " + (g - 1) + "，文件 " + dups.Count;
                });
            }
            catch (Exception ex)
            {
                Ui(() => { MsgText.Text = "失败：" + ex.Message; });
            }
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        private void Ui(Action a)
        {
            Dispatcher.BeginInvoke(a);
        }

        private void Locate_Click(object sender, RoutedEventArgs e)
        {
            var row = ResultList.SelectedItem as DupRow;
            if (row == null) return;
            try { Process.Start("explorer.exe", "/select,\"" + row.Path + "\""); }
            catch (Exception ex) { MsgText.Text = ex.Message; }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var row = ResultList.SelectedItem as DupRow;
            if (row == null) return;
            if (!ConfirmHelper.Delete(row.Path)) return;
            try
            {
                File.Delete(row.Path);
                MsgText.Text = "已删除";
            }
            catch (Exception ex) { MsgText.Text = "删除失败：" + ex.Message; }
        }

        private sealed class DupRow
        {
            public int Group { get; set; }
            public string Path { get; set; }
            public string SizeText { get; set; }
            public string HashShort { get; set; }
        }
    }
}
