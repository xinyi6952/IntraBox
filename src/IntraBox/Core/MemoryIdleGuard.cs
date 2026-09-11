using System;
using System.Windows;
using System.Windows.Threading;

namespace IntraBox.Core
{
    /// <summary>主窗口向空闲压缩器登记的停泊/恢复动作。</summary>
    public interface IMemoryHost
    {
        void ParkCurrentResources();
        void UnparkCurrentResources();
        bool HasEmptyWorkspace();
        void RestoreLastModuleNow();
        void RefreshMemoryText();
        bool TryUnloadCurrentSilent();
        bool IsBusyForTrim();
    }

    /// <summary>
    /// 托盘驻留内存压缩：仅在标题栏关闭选「托盘」之后进入停泊态。
    /// 截图覆盖层 Hide 主窗不会进入此态，避免标注时误卸模块。
    /// 前台不 Trim；阈值与空闲超时都有冷却和滞回。
    /// </summary>
    public static class MemoryIdleGuard
    {
        private static DispatcherTimer _timer;
        private static IMemoryHost _host;
        private static DateTime _parkedUtc;
        private static DateTime _lastTrimUtc;
        private static bool _unloadedThisPark;

        /// <summary>主窗口已按「藏到托盘」路径隐藏（不是截图临时 Hide）。</summary>
        public static bool IsParked { get; private set; }

        public static void Attach(IMemoryHost host)
        {
            _host = host;
        }

        public static void NotifyParked()
        {
            IsParked = true;
            _parkedUtc = DateTime.UtcNow;
            _unloadedThisPark = false;
            EnsureTimer();
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Start();
            }
            if (_host != null) _host.ParkCurrentResources();
        }

        public static void NotifyResumed()
        {
            if (!IsParked) return;
            IsParked = false;
            if (_timer != null) _timer.Stop();
            if (_host == null) return;
            _host.UnparkCurrentResources();
            if (_host.HasEmptyWorkspace()) _host.RestoreLastModuleNow();
            _host.RefreshMemoryText();
        }

        public static void Stop()
        {
            IsParked = false;
            _host = null;
            if (_timer == null) return;
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }

        internal static void MarkTrimmedNow()
        {
            _lastTrimUtc = DateTime.UtcNow;
        }

        private static void EnsureTimer()
        {
            if (_timer != null) return;
            var disp = Application.Current != null ? Application.Current.Dispatcher : null;
            if (disp == null) return;
            _timer = new DispatcherTimer(DispatcherPriority.Background, disp);
            _timer.Interval = TimeSpan.FromSeconds(15);
            _timer.Tick += Timer_Tick;
        }

        private static void Timer_Tick(object sender, EventArgs e)
        {
            if (!CanTick()) return;
            var s = ConfigManager.Instance.Settings;
            int idleSec = MemoryTrimPolicy.ClampIdleSec(s.TrayIdleReleaseSec);
            int high = MemoryTrimPolicy.ClampHighMb(s.MemoryTrimHighMb);
            int low = MemoryTrimPolicy.ClampLowMb(s.MemoryTrimLowMb, high);
            double hidden = (DateTime.UtcNow - _parkedUtc).TotalSeconds;
            double sinceTrim = _lastTrimUtc.Ticks == 0
                ? MemoryTrimPolicy.TrimCooldownSec
                : (DateTime.UtcNow - _lastTrimUtc).TotalSeconds;

            bool idleDue = MemoryTrimPolicy.ShouldIdleTrim(hidden, idleSec, sinceTrim, MemoryTrimPolicy.TrimCooldownSec);
            if (!_unloadedThisPark && s.TrayIdleUnloadModule && hidden >= idleSec)
            {
                if (_host != null && _host.TryUnloadCurrentSilent())
                    _unloadedThisPark = true;
            }

            long wsMb = 0;
            try { wsMb = MemoryUsage.Capture().WorkingSetBytes / 1024L / 1024L; }
            catch { }

            if (sinceTrim < MemoryTrimPolicy.TrimCooldownSec) return;
            bool needTrim = idleDue || MemoryTrimPolicy.ShouldThresholdTrim(wsMb, high, low);
            if (!needTrim) return;
            if (wsMb <= low && !idleDue) return;

            string reason = idleDue ? "tray-idle" : "tray-threshold";
            if (_unloadedThisPark) reason += "+unloaded";
            GcHelper.CollectAndTrimNow(reason);
            if (_host != null) _host.RefreshMemoryText();
        }

        private static bool CanTick()
        {
            bool busy = _host != null && _host.IsBusyForTrim();
            return MemoryTrimPolicy.CanAutoAct(IsParked, ConfirmHelper.IsExiting, busy, false);
        }
    }
}
