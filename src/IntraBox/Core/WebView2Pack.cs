using System;
using System.Diagnostics;
using System.IO;

namespace IntraBox.Core
{
    /// <summary>
    /// 随包 Edge 以内核 CAB 形式放在程序目录，用到预览时再解压。
    /// 解压结果仍是 exe 旁的 WebView2Runtime，Win7 不走这里。
    /// </summary>
    public static class WebView2Pack
    {
        public const string CabFileName = "WebView2Runtime.cab";
        public const string CabVersionFileName = "WebView2Runtime.cab.ver";
        public const string StampFileName = "intrabox.packver";
        public const int ChoiceAsk = 0;
        public const int ChoiceUse = 1;
        public const int ChoiceSkip = 2;

        private const long ExtractNeedBytes = 750L * 1024L * 1024L;
        private static readonly object Gate = new object();
        private static volatile bool _extractCancel;
        private static Process _expandProcess;

        public static int NormalizeChoice(int choice)
        {
            if (choice < ChoiceAsk || choice > ChoiceSkip) return ChoiceAsk;
            return choice;
        }

        public static string FindCab(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return null;
            string path = Path.Combine(baseDir, CabFileName);
            if (!File.Exists(path)) return null;
            return Path.GetFullPath(path);
        }

        public static string CabVersionPath(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return null;
            return Path.Combine(baseDir, CabVersionFileName);
        }

        public static string RuntimeRoot(string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return null;
            return Path.Combine(baseDir, WinCompatHelper.WebView2FolderName);
        }

        public static string ReadCabVersion(string baseDir)
        {
            return ReadFirstLine(CabVersionPath(baseDir));
        }

        public static string ReadStamp(string baseDir)
        {
            string root = RuntimeRoot(baseDir);
            if (string.IsNullOrEmpty(root)) return "";
            return ReadFirstLine(Path.Combine(root, StampFileName));
        }

        /// <summary>已有内核但没有版本标记时，记成当前 CAB 版本，不覆盖文件。</summary>
        public static void AdoptStampIfMissing(string baseDir, string cabVersion)
        {
            if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(cabVersion)) return;
            if (!string.IsNullOrEmpty(WinCompatHelper.FindWebView2FixedFolder(baseDir))
                && string.IsNullOrEmpty(ReadStamp(baseDir)))
                WriteStamp(baseDir, cabVersion);
        }

        public static void WriteStamp(string baseDir, string cabVersion)
        {
            string root = RuntimeRoot(baseDir);
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(cabVersion)) return;
            try
            {
                if (!Directory.Exists(root)) return;
                File.WriteAllText(Path.Combine(root, StampFileName), cabVersion.Trim());
            }
            catch (Exception ex)
            {
                CompatDegrade.Once("wv2-stamp", "写入 Edge 版本标记失败。", ex);
            }
        }

        public static int CompareVersion(string left, string right)
        {
            int[] a = ParseVersion(left);
            int[] b = ParseVersion(right);
            int n = a.Length > b.Length ? a.Length : b.Length;
            for (int i = 0; i < n; i++)
            {
                int av = i < a.Length ? a[i] : 0;
                int bv = i < b.Length ? b[i] : 0;
                if (av != bv) return av > bv ? 1 : -1;
            }
            return 0;
        }

        /// <summary>已解压版本比 CAB 旧，且这次 CAB 还没被拒绝过。</summary>
        public static bool ShouldOfferUpgrade(string extractedStamp, string cabVersion, string declinedVersion)
        {
            if (string.IsNullOrEmpty(cabVersion) || string.IsNullOrEmpty(extractedStamp)) return false;
            if (!string.IsNullOrEmpty(declinedVersion)
                && string.Equals(declinedVersion.Trim(), cabVersion.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            return CompareVersion(cabVersion, extractedStamp) > 0;
        }

        /// <summary>开始一次新的解压前清掉上次取消。须在显示「取消」之前调用。</summary>
        public static void ArmExtract()
        {
            _extractCancel = false;
        }

        /// <summary>取消正在进行的 expand。不改预览选项。</summary>
        public static void CancelExtract()
        {
            _extractCancel = true;
            try
            {
                Process p = _expandProcess;
                if (p != null && !p.HasExited) p.Kill();
            }
            catch { }
        }

        public static bool IsExtractCancelled(string error)
        {
            return !string.IsNullOrEmpty(error) && error.IndexOf("已取消解压", StringComparison.Ordinal) >= 0;
        }

        /// <summary>已解压则直接成功。否则把程序目录里的 CAB 解到 WebView2Runtime。</summary>
        public static bool TryExtract(string baseDir, out string error)
        {
            error = null;
            lock (Gate)
            {
                if (_extractCancel)
                {
                    error = "已取消解压。";
                    return false;
                }
                string ready = WinCompatHelper.FindWebView2FixedFolder(baseDir);
                if (!string.IsNullOrEmpty(ready)) return true;
                string root = RuntimeRoot(baseDir);
                if (!ExtractCabTo(baseDir, root, true, out error)) return false;
                WriteStamp(baseDir, ReadCabVersion(baseDir));
                return true;
            }
        }

        /// <summary>解到临时目录，确认有内核后再换上旧目录。失败时留下原来的目录。</summary>
        public static bool TryReplaceRuntime(string baseDir, out string error)
        {
            error = null;
            lock (Gate)
            {
                if (_extractCancel)
                {
                    error = "已取消解压。";
                    return false;
                }
                string root = RuntimeRoot(baseDir);
                if (string.IsNullOrEmpty(root))
                {
                    error = "没有程序目录。";
                    return false;
                }
                string incoming = root + ".incoming";
                string backup = root + ".bak";
                if (!ExtractCabTo(baseDir, incoming, true, out error)) return false;

                try
                {
                    if (Directory.Exists(backup)) Directory.Delete(backup, true);
                    if (Directory.Exists(root)) Directory.Move(root, backup);
                    Directory.Move(incoming, root);
                }
                catch (Exception ex)
                {
                    error = "替换 Edge 目录失败：" + ex.Message;
                    try
                    {
                        if (!Directory.Exists(root) && Directory.Exists(backup))
                            Directory.Move(backup, root);
                    }
                    catch { }
                    TryDeleteDir(incoming);
                    return false;
                }

                TryDeleteDir(backup);
                WriteStamp(baseDir, ReadCabVersion(baseDir));
                return !string.IsNullOrEmpty(WinCompatHelper.FindWebView2FixedFolder(baseDir));
            }
        }

        private static bool ExtractCabTo(string baseDir, string root, bool deleteIncomplete, out string error)
        {
            error = null;
            string cab = FindCab(baseDir);
            if (string.IsNullOrEmpty(cab))
            {
                error = "未找到 " + CabFileName + "。";
                return false;
            }
            if (string.IsNullOrEmpty(root))
            {
                error = "没有解压目录。";
                return false;
            }

            if (deleteIncomplete && Directory.Exists(root))
            {
                if (!string.IsNullOrEmpty(WinCompatHelper.FindWebView2ExeDir(root)))
                    return true;
                try { Directory.Delete(root, true); }
                catch (Exception ex)
                {
                    error = "无法清理解压目录：" + ex.Message;
                    return false;
                }
            }

            string drive = Path.GetPathRoot(Path.GetFullPath(baseDir));
            try
                {
                    var info = new DriveInfo(drive);
                    if (info.IsReady && info.AvailableFreeSpace < ExtractNeedBytes)
                    {
                        error = "磁盘剩余空间不足。解压 Edge 大约需要 750 MB。";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    CompatDegrade.Once("wv2-disk", "检查解压空间失败，仍尝试解压。", ex);
                }

            Directory.CreateDirectory(root);

                string expand = Path.Combine(Environment.SystemDirectory, "expand.exe");
                if (!File.Exists(expand))
                {
                    error = "系统没有 expand.exe，无法解压 Edge。";
                    return false;
                }

                try
                {
                    var psi = new ProcessStartInfo();
                    psi.FileName = expand;
                    psi.Arguments = "\"" + cab + "\" -F:* \"" + root + "\"";
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    using (Process p = Process.Start(psi))
                    {
                        if (p == null)
                        {
                            error = "无法启动解压。";
                            return false;
                        }
                        _expandProcess = p;
                        var started = DateTime.UtcNow;
                        while (!p.WaitForExit(400))
                        {
                            if (_extractCancel)
                            {
                                try { p.Kill(); } catch { }
                                _expandProcess = null;
                                error = "已取消解压。";
                                return false;
                            }
                            if ((DateTime.UtcNow - started).TotalMinutes >= 20)
                            {
                                try { p.Kill(); } catch { }
                                _expandProcess = null;
                                error = "解压超时。";
                                return false;
                            }
                        }
                        _expandProcess = null;
                        if (_extractCancel)
                        {
                            error = "已取消解压。";
                            return false;
                        }
                        if (p.ExitCode != 0)
                        {
                            error = "解压失败（expand 退出码 " + p.ExitCode + "）。";
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = "解压失败：" + ex.Message;
                    return false;
                }

                if (string.IsNullOrEmpty(WinCompatHelper.FindWebView2ExeDir(root)))
                {
                    error = "解压完成但未找到 " + WinCompatHelper.WebView2ExeName + "。";
                    return false;
                }
            return true;
        }

        private static int[] ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new int[0];
            string[] parts = text.Trim().Split('.');
            var nums = new System.Collections.Generic.List<int>();
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i], out n) || n < 0) break;
                nums.Add(n);
            }
            return nums.ToArray();
        }

        private static string ReadFirstLine(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
                string text = File.ReadAllText(path);
                if (string.IsNullOrEmpty(text)) return "";
                if (text[0] == '\uFEFF') text = text.Substring(1);
                int cut = text.IndexOfAny(new[] { '\r', '\n' });
                if (cut >= 0) text = text.Substring(0, cut);
                return text.Trim();
            }
            catch
            {
                return "";
            }
        }

        private static void TryDeleteDir(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch { }
        }
    }
}
