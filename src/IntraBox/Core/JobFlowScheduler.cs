using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace IntraBox.Core
{
    /// <summary>进程在（主窗或托盘）时到点执行编排；退出不补跑。同一编排重入跳过。</summary>
    public static class JobFlowScheduler
    {
        private static DispatcherTimer _timer;
        private static readonly HashSet<string> _running = new HashSet<string>();
        private static readonly object Sync = new object();

        public static void Start()
        {
            if (_timer != null) return;
            var disp = Application.Current != null ? Application.Current.Dispatcher : null;
            if (disp == null) return;
            _timer = new DispatcherTimer(DispatcherPriority.Background, disp);
            _timer.Interval = TimeSpan.FromSeconds(30);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        public static void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
                _timer = null;
            }
            lock (Sync) { _running.Clear(); }
        }

        public static bool IsRunning(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (Sync) { return _running.Contains(uid); }
        }

        public static bool TryBegin(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (Sync)
            {
                if (_running.Contains(uid)) return false;
                _running.Add(uid);
                return true;
            }
        }

        public static void End(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            lock (Sync) { _running.Remove(uid); }
        }

        private static void Timer_Tick(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            List<JobItem> jobs;
            try { jobs = JobFlowStore.Snapshot(); }
            catch { return; }
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                if (!JobScheduleHelper.ShouldAutoRun(job, now)) continue;
                StartAuto(job);
            }
        }

        private static void StartAuto(JobItem job)
        {
            if (job == null || string.IsNullOrEmpty(job.Uid)) return;
            if (!TryBegin(job.Uid)) return;
            JobFlowStore.MarkAutoAttempt(job.Uid);
            AppLog.Info("编排自动运行：" + (job.Name ?? job.Uid));
            var inputs = JobScheduleHelper.DefaultInputs(job);
            Task.Run(() =>
            {
                JobRunResult result;
                try
                {
                    result = JobRunner.Run(job, inputs, false, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    AppLog.Error("编排自动运行异常：" + (job.Name ?? job.Uid), ex);
                    result = new JobRunResult
                    {
                        Ok = false,
                        Message = "异常：" + ex.RootMessage(),
                        Lines = new List<string> { ex.RootMessage() }
                    };
                }
                try { JobFlowStore.UpdateRunResult(job.Uid, result, true); }
                catch (Exception ex) { AppLog.Error("编排运行结果写回失败：" + (job.Name ?? job.Uid), ex); }
                finally
                {
                    End(job.Uid);
                    try
                    {
                        var disp = Application.Current != null ? Application.Current.Dispatcher : null;
                        if (disp != null)
                            disp.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => GcHelper.CollectAndTrimNow("jobflow-auto")));
                        else
                            GcHelper.CollectAndTrimNow("jobflow-auto");
                    }
                    catch { }
                }
                AppLog.Info("编排自动运行结束：" + (job.Name ?? job.Uid) + " → " + (result != null && result.Ok ? "成功" : "失败"));
            });
        }
    }
}
