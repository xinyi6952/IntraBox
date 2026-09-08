using System;

namespace IntraBox.Modules.Todo
{
    public static class TodoRemindKind
    {
        public const int Daily = 0;
        public const int Off = 1;
        public const int Weekdays = 2;
        public const int Weekly = 3;
        public const int EveryNDays = 4;
        public const int DueDay = 5;

        public static string Label(int kind)
        {
            switch (kind)
            {
                case Off: return "关闭";
                case Weekdays: return "仅工作日";
                case Weekly: return "每周";
                case EveryNDays: return "每 N 天";
                case DueDay: return "仅计划完成当天";
                default: return "每天";
            }
        }
    }

    public static class TodoPriority
    {
        public const int Low = 0;
        public const int Normal = 1;
        public const int High = 2;

        public static string Label(int p)
        {
            if (p == Low) return "低";
            if (p == High) return "高";
            return "中";
        }
    }

    /// <summary>到点后重复：默认 3 次、间隔 10 分钟（闹钟式）。</summary>
    public static class TodoRemindRepeat
    {
        public const int DefaultTimes = 3;
        public const int DefaultIntervalMin = 10;
        /// <summary>每次到点后允许弹出的宽限（分钟）。超时不补弹，等下一档。</summary>
        public const int FireWindowMin = 2;
        public const int MaxTimes = 9;
        public const int MaxIntervalMin = 60;

        public static int ClampTimes(int n)
        {
            if (n < 1) return DefaultTimes;
            if (n > MaxTimes) return MaxTimes;
            return n;
        }

        public static int ClampIntervalMin(int n)
        {
            if (n < 1) return DefaultIntervalMin;
            if (n > MaxIntervalMin) return MaxIntervalMin;
            return n;
        }
    }

    /// <summary>计划完成日期早于当天则为过期（只比日期、不比时刻）。</summary>
    public static class TodoDue
    {
        public static bool IsDatePast(DateTime? due)
        {
            return IsDatePast(due, DateTime.Today);
        }

        public static bool IsDatePast(DateTime? due, DateTime today)
        {
            return due.HasValue && due.Value.Date < today.Date;
        }

        /// <summary>选「仅计划完成当天」时必须填写计划完成时间。</summary>
        public static bool DueDayNeedsDue(int remindKind, DateTime? due)
        {
            return remindKind == TodoRemindKind.DueDay && !due.HasValue;
        }

        public const string DueDayMissingMessage = "选择「仅计划完成当天」时请填写计划完成时间";
    }

    public sealed class TodoIndexFile
    {
        public int Version { get; set; }
        public bool SampleSeeded { get; set; }
        public int SampleRev { get; set; }
        public System.Collections.Generic.List<TodoItem> Items { get; set; }
    }

    /// <summary>任务元数据。详情富文本在 todos\{uid}\details.xaml。</summary>
    public sealed class TodoItem
    {
        public string Uid { get; set; }
        public string Id { get; set; }
        public int Priority { get; set; }
        public DateTime? DueAt { get; set; }
        public bool Completed { get; set; }
        public int RemindKind { get; set; }
        public int RemindHour { get; set; }
        public int RemindMinute { get; set; }
        public int RemindNDays { get; set; }
        public int RemindWeekday { get; set; }
        /// <summary>到点后共提醒几次（含首次）。缺省或 0 按 3 次。</summary>
        public int RemindTimes { get; set; }
        /// <summary>重复间隔分钟。缺省或 0 按 10 分钟。</summary>
        public int RemindIntervalMin { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastRemindedAt { get; set; }
        /// <summary>该日不再弹出提醒；次日仍按原频次。只比日期。</summary>
        public DateTime? MuteRemindOn { get; set; }
        public DateTime? CompletedAt { get; set; }
        /// <summary>锁定后只能查看，不能编辑或删除；即使勾选自动保存也不写入。已办本身只读。</summary>
        public bool ReadOnly { get; set; }

        public static TodoItem CreateNew(string id)
        {
            var now = DateTime.Now;
            return new TodoItem
            {
                Uid = Guid.NewGuid().ToString("N"),
                Id = id,
                Priority = TodoPriority.Normal,
                RemindKind = TodoRemindKind.Daily,
                RemindHour = 15,
                RemindMinute = 0,
                RemindNDays = 2,
                RemindWeekday = 1,
                RemindTimes = TodoRemindRepeat.DefaultTimes,
                RemindIntervalMin = TodoRemindRepeat.DefaultIntervalMin,
                CreatedAt = now,
                UpdatedAt = now
            };
        }
    }
}
