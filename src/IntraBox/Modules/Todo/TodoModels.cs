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
                case DueDay: return "仅到期当天";
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
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastRemindedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

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
                CreatedAt = now,
                UpdatedAt = now
            };
        }
    }
}
