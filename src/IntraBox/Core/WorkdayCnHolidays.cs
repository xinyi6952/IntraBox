using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>中国大陆国务院放假安排（内置，随版本更新）。off=放假，work=调休上班。</summary>
    public static class WorkdayCnHolidays
    {
        public static readonly int[] Years = { 2025, 2026 };

        public static string YearsText()
        {
            if (Years == null || Years.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Years.Length; i++)
            {
                if (i > 0) sb.Append("、");
                sb.Append(Years[i]);
            }
            return sb.ToString();
        }

        public static bool HasYear(int year)
        {
            for (int i = 0; i < Years.Length; i++)
                if (Years[i] == year) return true;
            return false;
        }

        public static void Fill(int year, HashSet<string> off, HashSet<string> work)
        {
            if (off == null || work == null) return;
            if (year == 2025) Fill2025(off, work);
            else if (year == 2026) Fill2026(off, work);
        }

        private static void Fill2025(HashSet<string> off, HashSet<string> work)
        {
            AddRange(off, 2025, 1, 1, 2025, 1, 1);
            AddRange(off, 2025, 1, 28, 2025, 2, 4);
            AddRange(off, 2025, 4, 4, 2025, 4, 6);
            AddRange(off, 2025, 5, 1, 2025, 5, 5);
            AddRange(off, 2025, 5, 31, 2025, 6, 2);
            AddRange(off, 2025, 10, 1, 2025, 10, 8);
            AddDay(work, 2025, 1, 26);
            AddDay(work, 2025, 2, 8);
            AddDay(work, 2025, 4, 27);
            AddDay(work, 2025, 9, 28);
            AddDay(work, 2025, 10, 11);
        }

        private static void Fill2026(HashSet<string> off, HashSet<string> work)
        {
            AddRange(off, 2026, 1, 1, 2026, 1, 3);
            AddRange(off, 2026, 2, 15, 2026, 2, 23);
            AddRange(off, 2026, 4, 4, 2026, 4, 6);
            AddRange(off, 2026, 5, 1, 2026, 5, 5);
            AddRange(off, 2026, 6, 19, 2026, 6, 21);
            AddRange(off, 2026, 9, 25, 2026, 9, 27);
            AddRange(off, 2026, 10, 1, 2026, 10, 7);
            AddDay(work, 2026, 1, 4);
            AddDay(work, 2026, 2, 14);
            AddDay(work, 2026, 2, 28);
            AddDay(work, 2026, 5, 9);
            AddDay(work, 2026, 9, 20);
            AddDay(work, 2026, 10, 10);
        }

        private static void AddDay(HashSet<string> set, int y, int m, int d)
        {
            set.Add(new DateTime(y, m, d).ToString("yyyy-MM-dd"));
        }

        private static void AddRange(HashSet<string> set, int y1, int m1, int d1, int y2, int m2, int d2)
        {
            var a = new DateTime(y1, m1, d1);
            var b = new DateTime(y2, m2, d2);
            for (var d = a; d <= b; d = d.AddDays(1))
                set.Add(d.ToString("yyyy-MM-dd"));
        }
    }
}
