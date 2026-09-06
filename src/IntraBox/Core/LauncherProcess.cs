using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace IntraBox.Core
{
    /// <summary>判断启动器目标是否已有进程在跑。不杀进程、不发起网络请求。</summary>
    public static class LauncherProcess
    {
        public static bool ShouldCheckAlreadyRunning(string kind, string target, string openWith)
        {
            if (kind == LauncherTarget.KindApp) return true;
            if (kind == LauncherTarget.KindFile && !string.IsNullOrWhiteSpace(openWith)) return true;
            if (kind == LauncherTarget.KindFile && LauncherTarget.IsBatPath(target)) return true;
            return false;
        }

        /// <summary>程序 / 指定打开方式对映像路径；.bat 另看窗口标题与文件占用。</summary>
        public static bool IsAlreadyRunning(string kind, string target, string openWith)
        {
            if (!ShouldCheckAlreadyRunning(kind, target, openWith)) return false;
            try
            {
                if (LauncherTarget.IsBatPath(target))
                {
                    if (BatFileInUse(target)) return true;
                    return MatchBatWindow(target, CollectWindowTitles());
                }
                string image = ResolveLaunchImage(kind, target, openWith);
                if (string.IsNullOrWhiteSpace(image)) return false;
                if (LauncherTarget.IsBatPath(image))
                {
                    if (BatFileInUse(image)) return true;
                    return MatchBatWindow(image, CollectWindowTitles());
                }
                return MatchProcessImages(image, CollectProcessImages());
            }
            catch
            {
                return false;
            }
        }

        public static string ResolveLaunchImage(string kind, string target, string openWith)
        {
            if (kind == LauncherTarget.KindFile && !string.IsNullOrWhiteSpace(openWith))
                return TryResolveShortcut(openWith.Trim());
            if (string.IsNullOrWhiteSpace(target)) return "";
            string t = target.Trim();
            if (kind == LauncherTarget.KindApp)
                return TryResolveShortcut(t);
            return t;
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            string t = path.Trim().Trim('"');
            try { return Path.GetFullPath(t); }
            catch { return t; }
        }

        public static bool MatchProcessImages(string imagePath, IList<string> processImages)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || processImages == null) return false;
            for (int i = 0; i < processImages.Count; i++)
            {
                if (SamePath(imagePath, processImages[i])) return true;
            }
            return false;
        }

        /// <summary>cmd 窗口标题含 bat 文件名则视为该脚本仍在跑。</summary>
        public static bool MatchBatWindow(string batPath, IList<string> windowTitles)
        {
            if (string.IsNullOrWhiteSpace(batPath) || windowTitles == null) return false;
            string name;
            try { name = Path.GetFileName(batPath.Trim()); }
            catch { return false; }
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < windowTitles.Count; i++)
            {
                string title = windowTitles[i];
                if (string.IsNullOrEmpty(title)) continue;
                if (title.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public static string TryResolveShortcut(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            string p = path.Trim();
            string ext;
            try { ext = Path.GetExtension(p); }
            catch { return p; }
            if (!string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase)) return p;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return p;
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { p });
                if (sc == null) return p;
                object dest = sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
                string s = dest as string;
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            catch
            {
            }
            return p;
        }

        private static List<string> CollectProcessImages()
        {
            var list = new List<string>();
            Process[] ps = null;
            try { ps = Process.GetProcesses(); }
            catch { return list; }
            if (ps == null) return list;
            for (int i = 0; i < ps.Length; i++)
            {
                Process p = ps[i];
                try
                {
                    string img = p.MainModule != null ? p.MainModule.FileName : null;
                    if (!string.IsNullOrWhiteSpace(img))
                        list.Add(img);
                }
                catch
                {
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
            return list;
        }

        private static List<string> CollectWindowTitles()
        {
            var list = new List<string>();
            try
            {
                var wins = Win32Native.ListVisibleWindows();
                if (wins == null) return list;
                for (int i = 0; i < wins.Count; i++)
                {
                    if (wins[i] != null && !string.IsNullOrEmpty(wins[i].Title))
                        list.Add(wins[i].Title);
                }
            }
            catch
            {
            }
            return list;
        }

        private static bool BatFileInUse(string batPath)
        {
            try
            {
                string path = NormalizePath(batPath);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                List<LockingProcess> procs;
                string err;
                if (!Win32Native.TryGetLockingProcesses(path, out procs, out err)) return false;
                return procs != null && procs.Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
