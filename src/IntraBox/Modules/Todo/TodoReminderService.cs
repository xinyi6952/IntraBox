using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using IntraBox.Core;

namespace IntraBox.Modules.Todo
{
    /// <summary>进程级待办提醒。已完成任务不弹。托盘驻留时仍检查。</summary>
    public static class TodoReminderService
    {
        private static DispatcherTimer _timer;
        private static readonly Dictionary<string, DateTime> _snooze = new Dictionary<string, DateTime>();
        private static readonly object _sync = new object();
        private static bool _promptOpen;
        private static readonly Queue<TodoItem> _queue = new Queue<TodoItem>();

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
            lock (_sync)
            {
                _queue.Clear();
                _snooze.Clear();
                _promptOpen = false;
            }
        }

        public static void Snooze(string uid, TimeSpan span)
        {
            if (string.IsNullOrEmpty(uid)) return;
            lock (_sync)
            {
                _snooze[uid] = DateTime.Now.Add(span);
            }
        }

        private static void Timer_Tick(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            var due = new List<TodoItem>();
            var items = TodoStore.Snapshot();
            for (int i = 0; i < items.Count; i++)
            {
                if (ShouldFire(items[i], now))
                    due.Add(items[i]);
            }
            if (due.Count == 0) return;
            lock (_sync)
            {
                for (int i = 0; i < due.Count; i++)
                    _queue.Enqueue(due[i]);
            }
            Pump();
        }

        private static void Pump()
        {
            TodoItem next = null;
            int remain;
            lock (_sync)
            {
                if (_promptOpen) return;
                if (_queue.Count == 0) return;
                next = _queue.Dequeue();
                remain = _queue.Count;
                _promptOpen = true;
            }
            ShowPrompt(next, remain);
        }

        private static void ShowPrompt(TodoItem item, int remain)
        {
            var w = new TodoReminderWindow(item, remain);
            w.Closed += (s, e) =>
            {
                lock (_sync) { _promptOpen = false; }
                Pump();
            };
            w.Show();
        }

        public static bool ShouldFire(TodoItem it, DateTime now)
        {
            if (it == null || it.Completed) return false;
            if (it.RemindKind == TodoRemindKind.Off) return false;
            DateTime snoozeTo;
            lock (_sync)
            {
                if (_snooze.TryGetValue(it.Uid, out snoozeTo) && now < snoozeTo)
                    return false;
            }
            if (!MatchesSchedule(it, now)) return false;
            if (it.LastRemindedAt.HasValue)
            {
                var last = it.LastRemindedAt.Value;
                if (last.Date == now.Date
                    && last.Hour == it.RemindHour
                    && Math.Abs((last - AtTime(now, it)).TotalMinutes) < 1)
                    return false;
                if (last.Date == now.Date && it.RemindKind != TodoRemindKind.Off)
                {
                    // 同一天已提醒过则不再弹
                    return false;
                }
            }
            var fireAt = AtTime(now, it);
            if (now < fireAt) return false;
            if ((now - fireAt).TotalMinutes > 30) return false;
            return true;
        }

        private static DateTime AtTime(DateTime now, TodoItem it)
        {
            return new DateTime(now.Year, now.Month, now.Day, it.RemindHour, it.RemindMinute, 0);
        }

        private static bool MatchesSchedule(TodoItem it, DateTime now)
        {
            switch (it.RemindKind)
            {
                case TodoRemindKind.Weekdays:
                    return now.DayOfWeek != DayOfWeek.Saturday && now.DayOfWeek != DayOfWeek.Sunday;
                case TodoRemindKind.Weekly:
                    return (int)now.DayOfWeek == it.RemindWeekday;
                case TodoRemindKind.EveryNDays:
                    int n = it.RemindNDays < 2 ? 2 : it.RemindNDays;
                    var start = it.CreatedAt.Date;
                    int days = (int)(now.Date - start).TotalDays;
                    if (days < 0) return false;
                    return days % n == 0;
                case TodoRemindKind.DueDay:
                    if (!it.DueAt.HasValue) return false;
                    return it.DueAt.Value.Date == now.Date;
                default:
                    return true;
            }
        }
    }
}
