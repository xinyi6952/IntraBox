using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    public enum WorkcalMark
    {
        None = 0,
        Off = 1,
        Work = 2
    }

    /// <summary>工作日：周一到周五 − 法定假 + 调休上班；用户覆盖优先于内置表。</summary>
    public static class WorkdayHelper
    {
        public static string Key(DateTime date)
        {
            return date.Date.ToString("yyyy-MM-dd");
        }

        public static bool HasBuiltinYear(int year)
        {
            return WorkdayCnHolidays.HasYear(year);
        }

        public static bool IsWorkday(DateTime date)
        {
            return IsWorkday(date, WorkcalStore.UserOff(), WorkcalStore.UserWork());
        }

        public static bool IsWorkday(DateTime date, ICollection<string> userOff, ICollection<string> userWork)
        {
            string k = Key(date);
            if (userWork != null && userWork.Contains(k)) return true;
            if (userOff != null && userOff.Contains(k)) return false;
            if (WorkcalStore.BuiltinWork(k)) return true;
            if (WorkcalStore.BuiltinOff(k)) return false;
            var w = date.DayOfWeek;
            return w != DayOfWeek.Saturday && w != DayOfWeek.Sunday;
        }

        public static WorkcalMark BuiltinMark(DateTime date)
        {
            string k = Key(date);
            if (WorkcalStore.BuiltinWork(k)) return WorkcalMark.Work;
            if (WorkcalStore.BuiltinOff(k)) return WorkcalMark.Off;
            return WorkcalMark.None;
        }

        public static WorkcalMark EffectiveMark(DateTime date)
        {
            string k = Key(date);
            if (WorkcalStore.UserWork().Contains(k)) return WorkcalMark.Work;
            if (WorkcalStore.UserOff().Contains(k)) return WorkcalMark.Off;
            return BuiltinMark(date);
        }

        /// <summary>从 start 起向前/后移动 |n| 个工作日，不计入 start 当天。</summary>
        public static DateTime AddWorkdays(DateTime start, int n)
        {
            return AddWorkdays(start, n, WorkcalStore.UserOff(), WorkcalStore.UserWork());
        }

        public static DateTime AddWorkdays(DateTime start, int n, ICollection<string> userOff, ICollection<string> userWork)
        {
            var d = start.Date;
            if (n == 0) return d;
            int step = n > 0 ? 1 : -1;
            int left = n > 0 ? n : -n;
            while (left > 0)
            {
                d = d.AddDays(step);
                if (IsWorkday(d, userOff, userWork)) left--;
            }
            return d;
        }

        public static int WorkdaysBetween(DateTime from, DateTime to)
        {
            return WorkdaysBetween(from, to, WorkcalStore.UserOff(), WorkcalStore.UserWork());
        }

        public static int WorkdaysBetween(DateTime from, DateTime to, ICollection<string> userOff, ICollection<string> userWork)
        {
            var a = from.Date;
            var b = to.Date;
            int sign = 1;
            if (b < a)
            {
                var t = a;
                a = b;
                b = t;
                sign = -1;
            }
            int n = 0;
            for (var d = a.AddDays(1); d <= b; d = d.AddDays(1))
            {
                if (IsWorkday(d, userOff, userWork)) n++;
            }
            return n * sign;
        }
    }
}
