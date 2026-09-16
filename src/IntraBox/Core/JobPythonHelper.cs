using System;
using System.Diagnostics;
using System.IO;

namespace IntraBox.Core
{
    /// <summary>解析本机 Python 解释器（py -3 / python / python3）。</summary>
    public static class JobPythonHelper
    {
        /// <summary>
        /// preferred 非空且文件存在则用之；否则在 PATH 上探测 py / python / python3。
        /// </summary>
        public static bool TryResolveExe(string preferred, out string exe, out string error)
        {
            exe = null;
            error = null;
            string pref = (preferred ?? "").Trim();
            if (pref.Length > 0)
            {
                if (File.Exists(pref))
                {
                    exe = pref;
                    return true;
                }
                error = "找不到指定的 Python 解释器：" + pref;
                return false;
            }
            string found;
            if (TryFindOnPath("py.exe", out found) || TryFindOnPath("py", out found))
            {
                if (ProbeLauncher(found, "-3"))
                {
                    exe = found;
                    return true;
                }
            }
            if (TryFindOnPath("python.exe", out found) || TryFindOnPath("python", out found))
            {
                exe = found;
                return true;
            }
            if (TryFindOnPath("python3.exe", out found) || TryFindOnPath("python3", out found))
            {
                exe = found;
                return true;
            }
            error = "未找到 Python（已试 py -3 / python / python3）。请安装 Python 或填写「Python 解释器」路径。";
            return false;
        }

        /// <summary>是否用 Windows py 启动器（需额外传 -3）。</summary>
        public static bool IsPyLauncher(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return false;
            string name = Path.GetFileNameWithoutExtension(exe);
            return string.Equals(name, "py", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryFindOnPath(string fileName, out string fullPath)
        {
            fullPath = null;
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] dirs = pathEnv.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < dirs.Length; i++)
            {
                try
                {
                    string dir = dirs[i].Trim().Trim('"');
                    if (dir.Length == 0) continue;
                    string candidate = Path.Combine(dir, fileName);
                    if (File.Exists(candidate))
                    {
                        fullPath = candidate;
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private static bool ProbeLauncher(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args + " -c \"print(1)\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (!p.WaitForExit(4000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
