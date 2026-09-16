using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace IntraBox.Core
{
    /// <summary>
    /// 目录大小：盘符概览 + 按层汇总子目录/文件体积。只统计，不删除。
    /// 符号链接/联接点不跟随，避免环与扫到意外交接。
    /// </summary>
    public static class DirSizeHelper
    {
        public const int MaxListedChildren = 2000;

        public sealed class DriveRow
        {
            public string Name { get; set; }
            public string Label { get; set; }
            public string Format { get; set; }
            public string UsedText { get; set; }
            public string FreeText { get; set; }
            public string TotalText { get; set; }
            public string PercentText { get; set; }
            public string Note { get; set; }
            public bool Ready { get; set; }
        }

        public sealed class Entry
        {
            public string Name { get; set; }
            public string FullPath { get; set; }
            public bool IsDirectory { get; set; }
            public long Bytes { get; set; }
            public int FileCount { get; set; }
            public int SkipCount { get; set; }
            public string KindText { get; set; }
            public string SizeText { get; set; }
            public string PercentText { get; set; }
            public string FileCountText { get; set; }
        }

        public sealed class ScanResult
        {
            public string Path { get; set; }
            public long Bytes { get; set; }
            public int FileCount { get; set; }
            public int SkipCount { get; set; }
            public int HiddenChildren { get; set; }
            public List<Entry> Children { get; set; }
        }

        public static List<DriveRow> ListDrives()
        {
            var list = new List<DriveRow>();
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch { return list; }
            for (int i = 0; i < drives.Length; i++)
            {
                var d = drives[i];
                var row = new DriveRow { Name = d.Name };
                try
                {
                    if (!d.IsReady)
                    {
                        row.Note = "未就绪";
                        row.Ready = false;
                        list.Add(row);
                        continue;
                    }
                    row.Ready = true;
                    row.Label = d.VolumeLabel ?? "";
                    row.Format = d.DriveFormat ?? "";
                    long total = d.TotalSize;
                    long free = d.TotalFreeSpace;
                    long used = total - free;
                    if (used < 0) used = 0;
                    row.UsedText = FormatSize(used);
                    row.FreeText = FormatSize(free);
                    row.TotalText = FormatSize(total);
                    row.PercentText = Percent(used, total);
                    row.Note = "";
                }
                catch
                {
                    row.Ready = false;
                    row.Note = "无法读取";
                }
                list.Add(row);
            }
            return list;
        }

        public static ScanResult ScanLevel(string path, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("目录不能为空");
            path = path.Trim();
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("目录不存在");

            var acc = new Acc();
            var children = new List<Entry>();
            ScanDir(path, acc, children, true, token);
            children.Sort(CompareEntry);
            int hidden = 0;
            if (children.Count > MaxListedChildren)
            {
                hidden = children.Count - MaxListedChildren;
                children.RemoveRange(MaxListedChildren, hidden);
            }
            FillTexts(children, acc.Bytes);
            return new ScanResult
            {
                Path = path,
                Bytes = acc.Bytes,
                FileCount = acc.FileCount,
                SkipCount = acc.SkipCount,
                HiddenChildren = hidden,
                Children = children
            };
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.0") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.0") + " MB";
            double gb = mb / 1024.0;
            if (gb < 1024) return gb.ToString("0.00") + " GB";
            return (gb / 1024.0).ToString("0.00") + " TB";
        }

        public static string Percent(long part, long total)
        {
            if (total <= 0 || part <= 0) return "0%";
            double p = part * 100.0 / total;
            if (p > 100) p = 100;
            if (p < 0.1 && part > 0) return "<0.1%";
            return p.ToString("0.0") + "%";
        }

        public static string ParentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                string full = Path.GetFullPath(path.Trim());
                var parent = Directory.GetParent(full);
                return parent == null ? null : parent.FullName;
            }
            catch
            {
                return null;
            }
        }

        public static string Summarize(ScanResult result)
        {
            if (result == null) return "";
            string msg = "合计 " + FormatSize(result.Bytes) + "，" + result.FileCount + " 个文件";
            if (result.SkipCount > 0)
                msg += "，跳过 " + result.SkipCount + " 项（无权限或链接）";
            if (result.HiddenChildren > 0)
                msg += "。仅列出最大的 " + MaxListedChildren + " 项";
            msg += "。本工具只汇总，不删除。";
            return msg;
        }

        private sealed class Acc
        {
            public long Bytes;
            public int FileCount;
            public int SkipCount;
        }

        private static void ScanDir(string path, Acc acc, List<Entry> children, bool storeChildren, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(path); }
            catch
            {
                acc.SkipCount++;
                files = null;
            }
            if (files != null)
            {
                foreach (string file in files)
                {
                    token.ThrowIfCancellationRequested();
                    long len;
                    try { len = new FileInfo(file).Length; }
                    catch
                    {
                        acc.SkipCount++;
                        continue;
                    }
                    if (len < 0) len = 0;
                    acc.Bytes += len;
                    acc.FileCount++;
                    if (storeChildren)
                    {
                        children.Add(new Entry
                        {
                            Name = Path.GetFileName(file),
                            FullPath = file,
                            IsDirectory = false,
                            Bytes = len,
                            FileCount = 1,
                            KindText = "文件"
                        });
                    }
                }
            }

            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(path); }
            catch
            {
                acc.SkipCount++;
                dirs = null;
            }
            if (dirs == null) return;

            foreach (string dir in dirs)
            {
                token.ThrowIfCancellationRequested();
                if (IsReparse(dir))
                {
                    acc.SkipCount++;
                    continue;
                }
                if (storeChildren)
                {
                    var sub = new Acc();
                    ScanDir(dir, sub, null, false, token);
                    acc.Bytes += sub.Bytes;
                    acc.FileCount += sub.FileCount;
                    acc.SkipCount += sub.SkipCount;
                    children.Add(new Entry
                    {
                        Name = Path.GetFileName(dir),
                        FullPath = dir,
                        IsDirectory = true,
                        Bytes = sub.Bytes,
                        FileCount = sub.FileCount,
                        SkipCount = sub.SkipCount,
                        KindText = "目录"
                    });
                }
                else
                {
                    ScanDir(dir, acc, null, false, token);
                }
            }
        }

        private static bool IsReparse(string path)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        private static int CompareEntry(Entry a, Entry b)
        {
            int c = b.Bytes.CompareTo(a.Bytes);
            if (c != 0) return c;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static void FillTexts(List<Entry> children, long total)
        {
            for (int i = 0; i < children.Count; i++)
            {
                var e = children[i];
                e.SizeText = FormatSize(e.Bytes);
                e.PercentText = Percent(e.Bytes, total);
                e.FileCountText = e.IsDirectory ? e.FileCount.ToString() : "";
            }
        }
    }
}
