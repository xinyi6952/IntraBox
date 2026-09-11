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
        private static readonly HashSet<string> _queuedUids = new HashSet<string>();
        private static readonly object _sync = new object();
        private static bool _promptOpen;
        private static string _showingUid;
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
                _queuedUids.Clear();
                _snooze.Clear();
                _promptOpen = false;
                _showingUid = null;
            }
        }

        public static void Snooze(string uid, TimeSpan span)
        {
            SnoozeUntil(uid, DateTime.Now.Add(span));
        }

        public static void SnoozeUntil(string uid, DateTime when)
        {
            if (string.IsNullOrEmpty(uid)) return;
            lock (_sync)
            {
                _snooze[uid] = when;
            }
        }

        public static void ClearSnooze(string uid)
        {
            ResetLiveRemind(uid);
        }

        /// <summary>改提醒规则后清掉稍后、排队中的同条，避免旧时刻再弹。</summary>
        public static void ResetLiveRemind(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            lock (_sync)
            {
                _snooze.Remove(uid);
                _queuedUids.Remove(uid);
                if (_queue.Count == 0) return;
                var keep = new Queue<TodoItem>();
                while (_queue.Count > 0)
                {
                    var x = _queue.Dequeue();
                    if (x != null && x.Uid != uid)
                        keep.Enqueue(x);
                }
                while (keep.Count > 0)
                    _queue.Enqueue(keep.Dequeue());
            }
        }

        /// <summary>今日不再提醒：落盘当天静音，并清掉稍后与排队。频次不变。</summary>
        public static void MuteToday(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            TodoStore.SetMuteRemindToday(uid, DateTime.Now);
            ResetLiveRemind(uid);
        }

        public static bool IsMutedToday(TodoItem it, DateTime now)
        {
            if (it == null || !it.MuteRemindOn.HasValue) return false;
            return it.MuteRemindOn.Value.Date == now.Date;
        }

        public static bool ShouldFire(TodoItem it, DateTime now)
        {
            if (it == null || it.Completed) return false;
            if (it.RemindKind == TodoRemindKind.Off) return false;
            if (IsMutedToday(it, now)) return false;
            if (TodoRemindRepeat.ExhaustedToday(it, now))
            {
                lock (_sync)
                {
                    if (!string.IsNullOrEmpty(it.Uid)) _snooze.Remove(it.Uid);
                }
                return false;
            }
            bool snoozeDue = false;
            lock (_sync)
            {
                DateTime snoozeTo;
                if (_snooze.TryGetValue(it.Uid, out snoozeTo))
                {
                    if (now < snoozeTo) return false;
                    snoozeDue = true;
                }
            }
            if (snoozeDue)
            {
                lock (_sync)
                {
                    _snooze.Remove(it.Uid);
                }
                return true;
            }
            if (!MatchesSchedule(it, now)) return false;
            DateTime first = AtTime(now, it);
            int times = TodoRemindRepeat.ClampTimes(it.RemindTimes);
            int every = TodoRemindRepeat.ClampIntervalMin(it.RemindIntervalMin);
            int grace = TodoRemindRepeat.FireWindowMin;
            if (grace > every) grace = every;
            for (int i = 0; i < times; i++)
            {
                DateTime slot = first.AddMinutes(i * every);
                DateTime slotEnd = slot.AddMinutes(grace);
                if (now < slot || now >= slotEnd) continue;
                if (SlotCovered(it.LastRemindedAt, slot)) continue;
                return true;
            }
            return false;
        }

        private static bool SlotCovered(DateTime? last, DateTime slot)
        {
            if (!last.HasValue) return false;
            if (last.Value.Date != slot.Date) return false;
            return last.Value >= slot;
        }

        /// <summary>到期项入队。同一 uid 已在队列则跳过；正在展示不挡同一任务的新档。</summary>
        public static int EnqueueDueItems(IList<TodoItem> due)
        {
            if (due == null || due.Count == 0) return 0;
            int added = 0;
            lock (_sync)
            {
                for (int i = 0; i < due.Count; i++)
                {
                    var it = due[i];
                    if (it == null || string.IsNullOrEmpty(it.Uid)) continue;
                    if (_queuedUids.Contains(it.Uid)) continue;
                    _queue.Enqueue(it);
                    _queuedUids.Add(it.Uid);
                    added++;
                }
            }
            return added;
        }

        public static int QueuedCount
        {
            get { lock (_sync) { return _queue.Count; } }
        }

        public static bool IsPromptOpen
        {
            get { lock (_sync) { return _promptOpen; } }
        }

        /// <summary>取出下一条并占住当前窗位；已有窗打开时不取。</summary>
        public static bool TryBeginPrompt(out TodoItem next, out int remain)
        {
            next = null;
            remain = 0;
            lock (_sync)
            {
                if (_promptOpen) return false;
                if (_queue.Count == 0) return false;
                next = _queue.Dequeue();
                if (next != null && !string.IsNullOrEmpty(next.Uid))
                    _queuedUids.Remove(next.Uid);
                remain = _queue.Count;
                _promptOpen = true;
                _showingUid = next != null ? next.Uid : null;
                return true;
            }
        }

        public static void EndPrompt()
        {
            lock (_sync)
            {
                _promptOpen = false;
                _showingUid = null;
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
            EnqueueDueItems(due);
            Pump();
        }

        private static void Pump()
        {
            TodoItem next;
            int remain;
            if (!TryBeginPrompt(out next, out remain)) return;
            ShowPrompt(next, remain);
        }

        private static void ShowPrompt(TodoItem item, int remain)
        {
            var w = new TodoReminderWindow(item, remain);
            w.Closed += (s, e) =>
            {
                EndPrompt();
                Pump();
            };
            w.Show();
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
