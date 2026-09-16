using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Microsoft.Win32;

namespace IntraBox.Core
{
    /// <summary>
    /// 护眼滤镜：独立于模块生命周期。退出/未处理异常时恢复伽马。
    /// </summary>
    public static class EyeCareService
    {
        private static DispatcherTimer _timer;
        private static DispatcherTimer _restTimer;
        private static DateTime? _pauseUntilUtc;
        private static DateTime _nextRestUtc = DateTime.MinValue;
        private static bool _applied;
        private static bool _lastApplyOk = true;
        private static string _lastError;
        private static bool _osHooked;

        public static event EventHandler Changed;
        public static event EventHandler RestDue;

        public static bool Enabled { get; private set; }
        public static bool Applied { get { return _applied; } }
        public static bool LastApplyOk { get { return _lastApplyOk; } }
        public static string LastError { get { return _lastError; } }

        public static DateTime? PauseUntilLocal
        {
            get { return _pauseUntilUtc.HasValue ? (DateTime?)_pauseUntilUtc.Value.ToLocalTime() : null; }
        }

        public static TimeSpan PauseRemaining
        {
            get
            {
                if (!_pauseUntilUtc.HasValue) return TimeSpan.Zero;
                TimeSpan left = _pauseUntilUtc.Value - DateTime.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        public static void StartFromSettings()
        {
            HookOs();
            var s = ConfigManager.Instance.Settings;
            if (EyeCareHelper.EnsureSegments(s, false))
                ConfigManager.Instance.Save();
            Enabled = s.EyeCareEnabled;
            EnsureTimer();
            Tick(false);
        }

        public static void SetEnabled(bool on)
        {
            Enabled = on;
            var s = ConfigManager.Instance.Settings;
            s.EyeCareEnabled = on;
            if (!on)
            {
                _pauseUntilUtc = null;
                StopRestTimer();
                EyeCareRamp.Restore(true);
                _applied = false;
                _lastApplyOk = true;
                _lastError = null;
            }
            ConfigManager.Instance.Save();
            EnsureTimer();
            Tick(false);
            Raise();
        }

        public static void PauseOneHour()
        {
            if (!Enabled) return;
            _pauseUntilUtc = DateTime.UtcNow.AddHours(1);
            EnsureTimer();
            Tick(false);
            Raise();
        }

        public static void CancelPause()
        {
            if (!Enabled) return;
            _pauseUntilUtc = null;
            EnsureTimer();
            Tick(true);
            Raise();
        }

        public static void TogglePause()
        {
            if (!Enabled) return;
            if (PauseRemaining.TotalSeconds >= 1)
                CancelPause();
            else
                PauseOneHour();
        }

        public static void SetRestRemind(bool rest)
        {
            var s = ConfigManager.Instance.Settings;
            EyeCareHelper.EnsureSegments(s, false);
            s.EyeCareRestRemind = rest;
            ConfigManager.Instance.Save();
            EnsureTimer();
            Raise();
        }

        public static bool TrySaveSegments(IList<EyeCareSegment> segments, out string error)
        {
            error = EyeCareHelper.ValidateSegments(segments);
            if (error != null) return false;
            var s = ConfigManager.Instance.Settings;
            s.EyeCareSegments = EyeCareHelper.CloneSegments(segments);
            EyeCareHelper.NormalizeSegmentList(s.EyeCareSegments);
            EyeCareHelper.SyncLegacyFields(s);
            ConfigManager.Instance.Save();
            EnsureTimer();
            Tick(true);
            Raise();
            return true;
        }

        public static void RestoreDefaults()
        {
            var s = ConfigManager.Instance.Settings;
            s.EyeCareSegments = EyeCareHelper.CreateDefaultSegments();
            s.EyeCareRestRemind = false;
            EyeCareHelper.SyncLegacyFields(s);
            _pauseUntilUtc = null;
            ConfigManager.Instance.Save();
            EnsureTimer();
            Tick(true);
            Raise();
        }

        /// <summary>退出或崩溃时写回原伽马，不改配置开关。</summary>
        public static void RestoreForExit()
        {
            try
            {
                StopTimers();
                EyeCareRamp.Restore(true);
                _applied = false;
            }
            catch { }
        }

        public static bool ShouldApplyNow(DateTime local)
        {
            if (!Enabled) return false;
            if (_pauseUntilUtc.HasValue && DateTime.UtcNow < _pauseUntilUtc.Value) return false;
            var s = ConfigManager.Instance.Settings;
            EyeCareSegment hit;
            return EyeCareHelper.TryPickSegment(local, s.EyeCareSegments, out hit);
        }

        private static void EnsureTimer()
        {
            if (!Enabled)
            {
                StopTimers();
                return;
            }
            if (_timer == null)
            {
                _timer = new DispatcherTimer();
                _timer.Tick += delegate { Tick(false); };
            }
            _timer.Interval = PauseRemaining.TotalSeconds >= 1
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(20);
            _timer.Start();
            var s = ConfigManager.Instance.Settings;
            if (s.EyeCareRestRemind)
            {
                if (_restTimer == null)
                {
                    _restTimer = new DispatcherTimer();
                    _restTimer.Interval = TimeSpan.FromSeconds(30);
                    _restTimer.Tick += OnRestTick;
                }
                if (_nextRestUtc == DateTime.MinValue)
                    _nextRestUtc = DateTime.UtcNow.AddMinutes(20);
                _restTimer.Start();
            }
            else
                StopRestTimer();
        }

        private static void StopTimers()
        {
            if (_timer != null) _timer.Stop();
            StopRestTimer();
        }

        private static void StopRestTimer()
        {
            if (_restTimer != null) _restTimer.Stop();
            _nextRestUtc = DateTime.MinValue;
        }

        private static void Tick(bool forceRewrite)
        {
            if (_pauseUntilUtc.HasValue && DateTime.UtcNow >= _pauseUntilUtc.Value)
                _pauseUntilUtc = null;
            if (_timer != null)
            {
                _timer.Interval = PauseRemaining.TotalSeconds >= 1
                    ? TimeSpan.FromSeconds(1)
                    : TimeSpan.FromSeconds(20);
            }

            bool want = ShouldApplyNow(DateTime.Now);
            if (!want)
            {
                if (_applied)
                {
                    EyeCareRamp.Restore(false);
                    _applied = false;
                    _lastApplyOk = true;
                    _lastError = null;
                }
                Raise();
                return;
            }

            var s = ConfigManager.Instance.Settings;
            EyeCareSegment hit;
            if (!EyeCareHelper.TryPickSegment(DateTime.Now, s.EyeCareSegments, out hit))
            {
                if (_applied)
                {
                    EyeCareRamp.Restore(false);
                    _applied = false;
                    _lastApplyOk = true;
                    _lastError = null;
                }
                Raise();
                return;
            }

            string err;
            if (EyeCareRamp.TryApply(EyeCareHelper.NormalizeKelvin(hit.Kelvin),
                    EyeCareHelper.NormalizeBrightness(hit.Brightness), out err))
            {
                _applied = true;
                _lastApplyOk = true;
                _lastError = null;
            }
            else
            {
                _applied = false;
                _lastApplyOk = false;
                _lastError = err;
            }
            Raise();
        }

        private static void OnRestTick(object sender, EventArgs e)
        {
            if (!ShouldApplyNow(DateTime.Now)) return;
            if (_nextRestUtc == DateTime.MinValue) _nextRestUtc = DateTime.UtcNow.AddMinutes(20);
            if (DateTime.UtcNow < _nextRestUtc) return;
            EventHandler h = RestDue;
            if (h != null) h(null, EventArgs.Empty);
            _nextRestUtc = DateTime.UtcNow.AddMinutes(20);
        }

        private static void HookOs()
        {
            if (_osHooked) return;
            _osHooked = true;
            SystemEvents.SessionSwitch += delegate { QueueTick(true); };
            SystemEvents.DisplaySettingsChanged += delegate { QueueTick(true); };
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) QueueTick(true);
        }

        private static void QueueTick(bool force)
        {
            try
            {
                DispatcherTimer t = _timer;
                if (t != null && t.Dispatcher != null && !t.Dispatcher.CheckAccess())
                {
                    t.Dispatcher.BeginInvoke((Action)(() => Tick(force)));
                    return;
                }
            }
            catch { }
            Tick(force);
        }

        private static void Raise()
        {
            EventHandler h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }
    }
}
