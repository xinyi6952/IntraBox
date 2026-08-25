using System;
using System.Globalization;
using System.Windows.Threading;

namespace IntraBox.Core
{
    /// <summary>
    /// 阻止系统睡眠：状态独立于模块生命周期，切换工具不复位；到期或退出程序时恢复。
    /// SetThreadExecutionState 必须在 UI 线程调用，定时器使用 DispatcherTimer。
    /// </summary>
    public static class KeepAwakeService
    {
        public const uint EsContinuous = 0x80000000;
        public const uint EsSystemRequired = 0x00000001;
        public const uint EsDisplayRequired = 0x00000002;

        private static DispatcherTimer _timer;
        private static DateTime? _untilUtc;

        public static event EventHandler Changed;

        public static bool IsEnabled { get; private set; }

        /// <summary>预计自动关闭的本地时间。未开启或未设时长时为 null。</summary>
        public static DateTime? UntilLocal
        {
            get { return _untilUtc.HasValue ? (DateTime?)_untilUtc.Value.ToLocalTime() : null; }
        }

        public static TimeSpan Remaining
        {
            get
            {
                if (!IsEnabled || !_untilUtc.HasValue) return TimeSpan.Zero;
                TimeSpan left = _untilUtc.Value - DateTime.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        public static void SetEnabled(bool on)
        {
            if (on)
            {
                _untilUtc = null;
                StopTimer();
                Apply(true);
            }
            else
            {
                _untilUtc = null;
                StopTimer();
                Apply(false);
            }
            Raise();
        }

        /// <summary>开启防止睡眠，到点后自动关闭。</summary>
        public static void Start(TimeSpan duration)
        {
            if (duration.TotalSeconds < 1)
            {
                SetEnabled(false);
                return;
            }
            _untilUtc = DateTime.UtcNow.Add(duration);
            Apply(true);
            EnsureTimer();
            _timer.Start();
            Raise();
        }

        public static bool TryParseDuration(string amount, string unit, out TimeSpan duration, out string error)
        {
            duration = TimeSpan.Zero;
            error = null;
            int n;
            if (string.IsNullOrWhiteSpace(amount)
                || !int.TryParse(amount.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
            {
                error = "请输入正整数时长。";
                return false;
            }
            if (n <= 0)
            {
                error = "时长必须大于 0。";
                return false;
            }

            bool hours = !string.IsNullOrEmpty(unit) && unit.IndexOf("小时", StringComparison.Ordinal) >= 0;
            if (hours)
            {
                if (n > 24 * 7)
                {
                    error = "最长 7 天（168 小时）。";
                    return false;
                }
                duration = TimeSpan.FromHours(n);
            }
            else
            {
                if (n > 24 * 7 * 60)
                {
                    error = "最长 7 天（10080 分钟）。";
                    return false;
                }
                duration = TimeSpan.FromMinutes(n);
            }
            return true;
        }

        private static void EnsureTimer()
        {
            if (_timer != null) return;
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += OnTick;
        }

        private static void StopTimer()
        {
            if (_timer != null) _timer.Stop();
        }

        private static void OnTick(object sender, EventArgs e)
        {
            if (_untilUtc.HasValue && DateTime.UtcNow >= _untilUtc.Value)
            {
                SetEnabled(false);
                return;
            }
            Raise();
        }

        private static void Apply(bool on)
        {
            if (on)
                Win32Native.SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
            else
                Win32Native.SetThreadExecutionState(EsContinuous);
            IsEnabled = on;
        }

        private static void Raise()
        {
            EventHandler h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }
    }
}
