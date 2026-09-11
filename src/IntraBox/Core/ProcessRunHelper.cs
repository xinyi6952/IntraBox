using System;
using System.Diagnostics;
using System.Threading;

namespace IntraBox.Core
{
    /// <summary>
    /// 启动控制台进程并读完标准输出；取消时结束子进程，避免切走工具后 netstat 等仍挂着。
    /// </summary>
    public static class ProcessRunHelper
    {
        public static string ReadStdout(ProcessStartInfo psi, CancellationToken token, int waitMs)
        {
            if (psi == null) throw new ArgumentNullException("psi");
            token.ThrowIfCancellationRequested();
            using (var p = Process.Start(psi))
            {
                if (p == null) throw new InvalidOperationException("无法启动进程");
                using (token.Register(delegate { TryKill(p); }))
                {
                    try
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        int wait = waitMs < 1 ? 1 : waitMs;
                        p.WaitForExit(wait);
                        token.ThrowIfCancellationRequested();
                        return output ?? "";
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        if (token.IsCancellationRequested)
                            throw new OperationCanceledException(token);
                        throw;
                    }
                }
            }
        }

        public static void TryKill(Process p)
        {
            if (p == null) return;
            try
            {
                if (!p.HasExited) p.Kill();
            }
            catch { }
        }
    }
}
