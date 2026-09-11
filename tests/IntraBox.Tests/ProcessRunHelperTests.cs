using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class ProcessRunHelperTests
    {
        [TestMethod]
        public void ReadStdout_取消时结束子进程()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ping.exe",
                Arguments = "-n 40 127.0.0.1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            var cts = new CancellationTokenSource();
            Exception caught = null;
            var thread = new Thread(() =>
            {
                try { ProcessRunHelper.ReadStdout(psi, cts.Token, 20000); }
                catch (Exception ex) { caught = ex; }
            });
            thread.IsBackground = true;
            thread.Start();
            Thread.Sleep(400);
            cts.Cancel();
            Assert.IsTrue(thread.Join(8000), "取消后子进程读取应结束");
            Assert.IsTrue(caught is OperationCanceledException, "取消应抛出 OperationCanceledException");
        }
    }
}
