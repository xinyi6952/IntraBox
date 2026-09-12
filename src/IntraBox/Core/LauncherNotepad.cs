using System;
using System.IO;

namespace IntraBox.Core
{
    /// <summary>系统记事本：每次打开一份新的空白「未命名.txt」，不复用已有窗口内容。</summary>
    public static class LauncherNotepad
    {
        public const string EmptyName = "未命名.txt";

        public static string ScratchRoot()
        {
            return Path.Combine(Path.GetTempPath(), "IntraBox", "notepad");
        }

        /// <summary>在临时目录新建空的未命名.txt。失败返回空串。</summary>
        public static string TryCreateEmptyDocument()
        {
            try
            {
                string root = ScratchRoot();
                Directory.CreateDirectory(root);
                SweepOld(root);
                string dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, EmptyName);
                File.WriteAllText(path, "");
                return path;
            }
            catch
            {
                return "";
            }
        }

        public static void SweepOld(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            DateTime cutoff = DateTime.UtcNow.AddDays(-1);
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return; }
            for (int i = 0; i < dirs.Length; i++)
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(dirs[i]) > cutoff) continue;
                    Directory.Delete(dirs[i], true);
                }
                catch
                {
                }
            }
        }
    }
}
