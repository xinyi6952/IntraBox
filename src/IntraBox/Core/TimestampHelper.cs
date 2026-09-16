using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>时间解析结果：一个绝对时刻 + 识别说明。</summary>
    public sealed class TimeParseResult
    {
        public DateTimeOffset Instant { get; set; }
        public string Kind { get; set; }
        public string Note { get; set; }
    }

    /// <summary>一种可读格式。</summary>
    public sealed class TimeFormatRow
    {
        public TimeFormatRow() { }
        public TimeFormatRow(string name, string value)
        {
            Name = name;
            Value = value;
        }
        public string Name { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    /// 时间转换：把日志/接口里常见的乱格式识别成一个时刻，再列出常用写法。
    /// Unix 换算保留给界面与旧测试。
    /// </summary>
    public static class TimestampHelper
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] ZhWeek =
        {
            "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"
        };
        private static readonly string[] EnMonthAbbrev =
        {
            "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };
        private static readonly string[] EnMonthFull =
        {
            "", "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };
        private static readonly string[] ZhMonth =
        {
            "", "一月", "二月", "三月", "四月", "五月", "六月",
            "七月", "八月", "九月", "十月", "十一月", "十二月"
        };
        private static readonly string[] LocalExact =
        {
            "yyyy-MM-dd HH:mm:ss.fffffff",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd HH:mm",
            "yyyy.MM.dd HH:mm:ss",
            "yyyy.MM.dd HH:mm",
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "yyyy.MM.dd",
            "yyyy年M月d日 H时m分s秒",
            "yyyy年M月d日 HH:mm:ss",
            "yyyy年M月d日 H:mm:ss",
            "yyyy年M月d日 HH:mm",
            "yyyy年M月d日",
            "yyyy年M月",
            "yyyy-MM",
            "dd MMM yyyy HH:mm:ss",
            "dd MMM yyyy HH:mm",
            "dd MMM yyyy",
            "d MMM yyyy HH:mm:ss",
            "d MMM yyyy",
            "MMM d, yyyy HH:mm:ss",
            "MMM d, yyyy HH:mm",
            "MMM d, yyyy",
            "MMMM d, yyyy HH:mm:ss",
            "MMMM d, yyyy",
            "d MMMM yyyy",
            "dd-MMM-yyyy HH:mm:ss",
            "dd-MMM-yyyy",
            "ddd MMM d yyyy HH:mm:ss",
            "ddd MMM dd yyyy HH:mm:ss",
            "ddd MMM d HH:mm:ss yyyy",
            "ddd MMM dd HH:mm:ss yyyy"
        };
        private static readonly string[] ZonedExact =
        {
            "yyyy-MM-dd'T'HH:mm:ss.fffffffK",
            "yyyy-MM-dd'T'HH:mm:ss.ffffffK",
            "yyyy-MM-dd'T'HH:mm:ss.fffK",
            "yyyy-MM-dd'T'HH:mm:ssK",
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            "yyyy-MM-dd HH:mm:ssK",
            "ddd, dd MMM yyyy HH:mm:ss K",
            "dd MMM yyyy HH:mm:ss K",
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "dd MMM yyyy HH:mm:ss zzz",
            "r"
        };

        public static DateTimeOffset FromUnixSeconds(long seconds)
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        public static long ToUnixSeconds(DateTimeOffset dto)
        {
            return dto.ToUnixTimeSeconds();
        }

        public static DateTimeOffset FromUnixMilliseconds(long milliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        public static long ToUnixMilliseconds(DateTimeOffset dto)
        {
            return dto.ToUnixTimeMilliseconds();
        }

        /// <summary>识别输入。now 只用于「只有星期/只有月」这类相对日期，测试可钉死。</summary>
        public static bool TryParse(string input, DateTimeOffset now, out TimeParseResult result, out string error)
        {
            result = null;
            error = null;
            string s = Normalize(input);
            if (s.Length == 0)
            {
                error = "请填写时间";
                return false;
            }

            TimeParseResult hit;
            if (TryDigits(s, now.Offset, out hit)) { result = hit; return true; }
            if (TryIsoWeek(s, now.Offset, out hit)) { result = hit; return true; }
            if (TryZonedExact(s, out hit)) { result = hit; return true; }
            if (TryLocalExact(s, now.Offset, out hit)) { result = hit; return true; }
            if (TrySlashDate(s, now.Offset, out hit)) { result = hit; return true; }
            if (TryCultures(s, now.Offset, out hit)) { result = hit; return true; }
            if (TryMonthYear(s, now, out hit)) { result = hit; return true; }
            if (TryWeekdayOnly(s, now, out hit)) { result = hit; return true; }
            if (TryMonthOnly(s, now, out hit)) { result = hit; return true; }

            error = "无法识别该时间。可试 Unix 秒/毫秒、ISO、RFC1123、中文日期、英文月名、2026-W38 或 2026-09。";
            return false;
        }

        public static List<TimeFormatRow> FormatRows(TimeParseResult parsed, TimeSpan localOffset)
        {
            var rows = new List<TimeFormatRow>();
            if (parsed == null) return rows;
            var instant = parsed.Instant;
            var local = instant.ToOffset(localOffset);
            var utc = instant.ToUniversalTime();
            int isoYear, isoWeek, isoDay;
            GetIsoWeek(local.DateTime, out isoYear, out isoWeek, out isoDay);

            rows.Add(new TimeFormatRow("识别", parsed.Kind ?? ""));
            if (!string.IsNullOrEmpty(parsed.Note))
                rows.Add(new TimeFormatRow("说明", parsed.Note));
            rows.Add(new TimeFormatRow("本地", local.ToString("yyyy-MM-dd HH:mm:ss", Inv)));
            rows.Add(new TimeFormatRow("UTC", utc.ToString("yyyy-MM-dd HH:mm:ss", Inv)));
            rows.Add(new TimeFormatRow("ISO 8601", local.ToString("yyyy-MM-dd'T'HH:mm:sszzz", Inv)));
            rows.Add(new TimeFormatRow("ISO UTC", utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv)));
            rows.Add(new TimeFormatRow("RFC 1123", utc.ToString("r", Inv)));
            rows.Add(new TimeFormatRow("中文", ChineseLong(local)));
            rows.Add(new TimeFormatRow("日期", local.ToString("yyyy-MM-dd", Inv)));
            rows.Add(new TimeFormatRow("时间", local.ToString("HH:mm:ss", Inv)));
            rows.Add(new TimeFormatRow("ISO 周", isoYear.ToString("D4", Inv) + "-W" + isoWeek.ToString("D2", Inv) + "-" + isoDay.ToString(Inv)));
            rows.Add(new TimeFormatRow("文件名", local.ToString("yyyyMMdd_HHmmss", Inv)));
            rows.Add(new TimeFormatRow("Unix 秒", instant.ToUnixTimeSeconds().ToString(Inv)));
            rows.Add(new TimeFormatRow("Unix 毫秒", instant.ToUnixTimeMilliseconds().ToString(Inv)));
            long us = instant.ToUnixTimeMilliseconds() * 1000L
                      + (instant.Ticks % TimeSpan.TicksPerMillisecond) / 10L;
            rows.Add(new TimeFormatRow("Unix 微秒", us.ToString(Inv)));
            return rows;
        }

        private static string ChineseLong(DateTimeOffset local)
        {
            int w = (int)local.DayOfWeek;
            return local.ToString("yyyy年M月d日 ", Inv) + ZhWeek[w] + local.ToString(" HH:mm:ss", Inv);
        }

        private static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            bool space = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\t' || c == '\r' || c == '\n') c = ' ';
                if (c == ' ')
                {
                    if (space) continue;
                    space = true;
                    sb.Append(' ');
                    continue;
                }
                space = false;
                sb.Append(c);
            }
            string s = sb.ToString().Trim();
            int par = s.LastIndexOf(" (", StringComparison.Ordinal);
            if (par > 0 && s.EndsWith(")", StringComparison.Ordinal))
                s = s.Substring(0, par).Trim();
            s = ReplaceNamedOffset(s, "GMT");
            s = ReplaceNamedOffset(s, "UTC");
            return s;
        }

        private static string ReplaceNamedOffset(string s, string prefix)
        {
            int i = IndexOfIgnore(s, prefix);
            if (i < 0) return s;
            int j = i + prefix.Length;
            if (j + 5 <= s.Length && (s[j] == '+' || s[j] == '-')
                && IsDigit(s[j + 1]) && IsDigit(s[j + 2]) && IsDigit(s[j + 3]) && IsDigit(s[j + 4]))
            {
                string off = s.Substring(j, 1) + s.Substring(j + 1, 2) + ":" + s.Substring(j + 3, 2);
                return (s.Substring(0, i) + off + s.Substring(j + 5)).Trim();
            }
            if (j == s.Length)
                return (s.Substring(0, i) + "+00:00").Trim();
            return s;
        }

        private static int IndexOfIgnore(string s, string needle)
        {
            return s.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static bool TryDigits(string s, TimeSpan offset, out TimeParseResult result)
        {
            result = null;
            string t = s;
            bool neg = false;
            if (t.Length > 0 && t[0] == '-')
            {
                neg = true;
                t = t.Substring(1);
            }
            if (t.Length == 0) return false;
            for (int i = 0; i < t.Length; i++)
            {
                if (!IsDigit(t[i])) return false;
            }
            long n;
            if (!long.TryParse(t, NumberStyles.None, Inv, out n)) return false;
            if (neg) n = -n;

            if (!neg && TryCompact(t, offset, out result)) return true;

            try
            {
                if (t.Length <= 10)
                {
                    result = Make(DateTimeOffset.FromUnixTimeSeconds(n), "Unix 秒", null);
                    return true;
                }
                if (t.Length <= 13)
                {
                    result = Make(DateTimeOffset.FromUnixTimeMilliseconds(n), "Unix 毫秒", null);
                    return true;
                }
                if (t.Length <= 16)
                {
                    long sec = n / 1000000L;
                    long remUs = n % 1000000L;
                    if (remUs < 0)
                    {
                        sec -= 1;
                        remUs += 1000000L;
                    }
                    var dto = DateTimeOffset.FromUnixTimeSeconds(sec).AddTicks(remUs * 10L);
                    result = Make(dto, "Unix 微秒", null);
                    return true;
                }
                if (t.Length <= 19)
                {
                    long sec = n / 1000000000L;
                    long remNs = n % 1000000000L;
                    if (remNs < 0)
                    {
                        sec -= 1;
                        remNs += 1000000000L;
                    }
                    var dto = DateTimeOffset.FromUnixTimeSeconds(sec).AddTicks(remNs / 100L);
                    result = Make(dto, "Unix 纳秒", null);
                    return true;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
            return false;
        }

        private static bool TryCompact(string digits, TimeSpan offset, out TimeParseResult result)
        {
            result = null;
            int len = digits.Length;
            if (len != 8 && len != 10 && len != 12 && len != 14) return false;
            int y, m, d, h = 0, min = 0, sec = 0;
            if (!TryInt(digits, 0, 4, out y) || y < 1970 || y > 2099) return false;
            if (!TryInt(digits, 4, 2, out m) || m < 1 || m > 12) return false;
            if (!TryInt(digits, 6, 2, out d) || d < 1 || d > DateTime.DaysInMonth(y, m)) return false;
            if (len >= 10)
            {
                if (!TryInt(digits, 8, 2, out h) || h > 23) return false;
            }
            if (len >= 12)
            {
                if (!TryInt(digits, 10, 2, out min) || min > 59) return false;
            }
            if (len >= 14)
            {
                if (!TryInt(digits, 12, 2, out sec) || sec > 59) return false;
            }
            result = Make(new DateTimeOffset(y, m, d, h, min, sec, offset), "紧凑数字", "按本地时区、缺的时分秒补 0。");
            return true;
        }

        private static bool TryInt(string s, int start, int len, out int v)
        {
            v = 0;
            return int.TryParse(s.Substring(start, len), NumberStyles.None, Inv, out v);
        }

        private static bool TryIsoWeek(string s, TimeSpan offset, out TimeParseResult result)
        {
            result = null;
            // 2026-W38 或 2026-W38-2
            if (s.Length < 8 || s[4] != '-' || (s[5] != 'W' && s[5] != 'w')) return false;
            int year, week, day = 1;
            if (!TryInt(s, 0, 4, out year)) return false;
            if (s.Length == 8)
            {
                if (!TryInt(s, 6, 2, out week)) return false;
            }
            else if (s.Length == 10 && s[8] == '-')
            {
                if (!TryInt(s, 6, 2, out week)) return false;
                if (!TryInt(s, 9, 1, out day)) return false;
            }
            else return false;
            if (week < 1 || week > 53 || day < 1 || day > 7) return false;
            DateTimeOffset monday = IsoWeekStart(year, week, offset);
            var dto = monday.AddDays(day - 1);
            string note = day == 1
                ? "ISO 周从周一起，缺星期几时按周一 00:00 本地。"
                : "ISO 周，当天 00:00 本地。";
            result = Make(dto, "ISO 周", note);
            return true;
        }

        private static DateTimeOffset IsoWeekStart(int year, int week, TimeSpan offset)
        {
            var jan4 = new DateTime(year, 1, 4);
            int jan4Iso = jan4.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)jan4.DayOfWeek;
            var monday1 = jan4.AddDays(1 - jan4Iso);
            var d = monday1.AddDays((week - 1) * 7);
            return new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, offset);
        }

        public static void GetIsoWeek(DateTime d, out int year, out int week, out int isoDay)
        {
            isoDay = d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;
            var thu = d.AddDays(4 - isoDay);
            year = thu.Year;
            var jan4 = new DateTime(year, 1, 4);
            int jan4Iso = jan4.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)jan4.DayOfWeek;
            var monday1 = jan4.AddDays(1 - jan4Iso);
            week = (int)((thu.Date - monday1).TotalDays / 7) + 1;
        }

        private static bool TryZonedExact(string s, out TimeParseResult result)
        {
            result = null;
            if (TryZonedExactOne(s, out result)) return true;
            string stripped;
            if (TryStripWeekdayPrefix(s, out stripped) && TryZonedExactOne(stripped, out result))
                return true;
            return false;
        }

        private static bool TryZonedExactOne(string s, out TimeParseResult result)
        {
            result = null;
            for (int i = 0; i < ZonedExact.Length; i++)
            {
                DateTimeOffset dto;
                if (!DateTimeOffset.TryParseExact(s, ZonedExact[i], Inv,
                    DateTimeStyles.AllowWhiteSpaces, out dto))
                    continue;
                result = Make(dto, "带时区", null);
                return true;
            }
            return false;
        }

        /// <summary>RFC 里星期写错时，丢掉前面的 Tue, 再按日期解析。</summary>
        private static bool TryStripWeekdayPrefix(string s, out string rest)
        {
            rest = null;
            int comma = s.IndexOf(',');
            if (comma < 3 || comma > 12) return false;
            for (int i = 0; i < comma; i++)
            {
                if (!char.IsLetter(s[i])) return false;
            }
            rest = s.Substring(comma + 1).Trim();
            return rest.Length >= 10;
        }

        private static bool TryLocalExact(string s, TimeSpan offset, out TimeParseResult result)
        {
            result = null;
            for (int i = 0; i < LocalExact.Length; i++)
            {
                DateTime dt;
                if (!DateTime.TryParseExact(s, LocalExact[i], Inv,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out dt))
                    continue;
                string kind = "本地日期时间";
                string note = null;
                if (LocalExact[i] == "yyyy年M月" || LocalExact[i] == "yyyy-MM")
                {
                    kind = "年月";
                    note = "缺日时分秒，按该月 1 日 00:00 本地。";
                }
                else if (LocalExact[i] == "yyyy-MM-dd" || LocalExact[i] == "yyyy/MM/dd"
                         || LocalExact[i] == "yyyy.MM.dd" || LocalExact[i] == "yyyy年M月d日"
                         || LocalExact[i] == "dd MMM yyyy" || LocalExact[i] == "d MMM yyyy"
                         || LocalExact[i] == "MMM d, yyyy" || LocalExact[i] == "MMMM d, yyyy"
                         || LocalExact[i] == "d MMMM yyyy" || LocalExact[i] == "dd-MMM-yyyy")
                {
                    note = "未写时刻，按 00:00 本地。";
                }
                result = Make(new DateTimeOffset(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, offset), kind, note);
                return true;
            }
            return false;
        }

        private static bool TrySlashDate(string s, TimeSpan offset, out TimeParseResult result)
        {
            result = null;
            // 9/16/2026 或 16/09/2026，避免月日对调
            string[] parts = s.Split(new[] { ' ' }, 2);
            string date = parts[0];
            string time = parts.Length > 1 ? parts[1] : "";
            string[] segs = date.Split('/', '.', '-');
            if (segs.Length != 3) return false;
            int a, b, y;
            if (!int.TryParse(segs[0], NumberStyles.None, Inv, out a)) return false;
            if (!int.TryParse(segs[1], NumberStyles.None, Inv, out b)) return false;
            if (!int.TryParse(segs[2], NumberStyles.None, Inv, out y)) return false;
            int month, day;
            if (segs[0].Length == 4)
            {
                y = a;
                month = b;
                if (!int.TryParse(segs[2], NumberStyles.None, Inv, out day)) return false;
            }
            else if (a > 12)
            {
                day = a;
                month = b;
            }
            else if (b > 12)
            {
                month = a;
                day = b;
            }
            else
            {
                // 都 ≤12：按年在最后的美式
                month = a;
                day = b;
            }
            if (y < 1 || month < 1 || month > 12) return false;
            if (day < 1 || day > DateTime.DaysInMonth(y, month)) return false;
            int h = 0, min = 0, sec = 0;
            if (time.Length > 0)
            {
                DateTime tm;
                if (!DateTime.TryParseExact(time, new[] { "HH:mm:ss", "H:mm:ss", "HH:mm", "H:mm", "hh:mm:ss tt", "h:mm:ss tt" },
                    Inv, DateTimeStyles.AllowWhiteSpaces, out tm))
                    return false;
                h = tm.Hour;
                min = tm.Minute;
                sec = tm.Second;
            }
            string note = time.Length == 0 ? "斜杠日期；未写时刻按 00:00 本地。" : "斜杠日期，按本地时区。";
            result = Make(new DateTimeOffset(y, month, day, h, min, sec, offset), "斜杠日期", note);
            return true;
        }

        private static bool TryCultures(string s, TimeSpan offset, out TimeParseResult result)
        {
            result = null;
            var cultures = new List<CultureInfo>();
            cultures.Add(Inv);
            AddCulture(cultures, "en-US");
            AddCulture(cultures, "zh-CN");
            for (int i = 0; i < cultures.Count; i++)
            {
                DateTimeOffset dto;
                if (DateTimeOffset.TryParse(s, cultures[i], DateTimeStyles.AllowWhiteSpaces, out dto))
                {
                    if (LooksUnspecifiedOffset(s))
                        dto = new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, dto.Second, dto.Millisecond, offset);
                    result = Make(dto, "常规解析", null);
                    return true;
                }
            }
            return false;
        }

        private static void AddCulture(List<CultureInfo> list, string name)
        {
            try { list.Add(CultureInfo.GetCultureInfo(name)); }
            catch (CultureNotFoundException) { }
        }

        private static bool LooksUnspecifiedOffset(string s)
        {
            if (s.IndexOf('Z') >= 0 || s.IndexOf('z') >= 0) return false;
            if (s.IndexOf('+') >= 0) return false;
            int colon = s.LastIndexOf(':');
            if (colon > 0 && colon + 3 <= s.Length && s[colon - 3] == '-') return false;
            return true;
        }

        private static bool TryMonthYear(string s, DateTimeOffset now, out TimeParseResult result)
        {
            result = null;
            string[] bits = s.Replace(',', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length != 2) return false;
            int month, year;
            if (TryMonthName(bits[0], out month) && int.TryParse(bits[1], NumberStyles.None, Inv, out year))
            { }
            else if (TryMonthName(bits[1], out month) && int.TryParse(bits[0], NumberStyles.None, Inv, out year))
            { }
            else return false;
            if (year < 1 || year > 9999) return false;
            result = Make(new DateTimeOffset(year, month, 1, 0, 0, 0, now.Offset), "年月", "缺日时分秒，按该月 1 日 00:00 本地。");
            return true;
        }

        private static bool TryWeekdayOnly(string s, DateTimeOffset now, out TimeParseResult result)
        {
            result = null;
            DayOfWeek dow;
            if (!TryWeekdayName(s, out dow)) return false;
            int today = (int)now.DayOfWeek;
            int want = (int)dow;
            int add = (want - today + 7) % 7;
            var d = now.Date.AddDays(add);
            result = Make(new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, now.Offset), "星期",
                "只有星期：从今天起（含今天）最近一个该日 00:00 本地。");
            return true;
        }

        private static bool TryMonthOnly(string s, DateTimeOffset now, out TimeParseResult result)
        {
            result = null;
            int month;
            if (!TryMonthName(s, out month)) return false;
            result = Make(new DateTimeOffset(now.Year, month, 1, 0, 0, 0, now.Offset), "月份",
                "只有月份：按今年该月 1 日 00:00 本地。");
            return true;
        }

        private static bool TryMonthName(string s, out int month)
        {
            month = 0;
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Length <= 3 && s.EndsWith("月", StringComparison.Ordinal))
            {
                int n;
                if (int.TryParse(s.Substring(0, s.Length - 1), NumberStyles.None, Inv, out n) && n >= 1 && n <= 12)
                {
                    month = n;
                    return true;
                }
            }
            for (int i = 1; i <= 12; i++)
            {
                if (string.Equals(s, EnMonthAbbrev[i], StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s, EnMonthFull[i], StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s, ZhMonth[i], StringComparison.Ordinal))
                {
                    month = i;
                    return true;
                }
            }
            return false;
        }

        private static bool TryWeekdayName(string s, out DayOfWeek dow)
        {
            dow = DayOfWeek.Sunday;
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Trim();
            string[] enFull = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
            string[] enAb = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            string[] zh1 = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
            string[] zh2 = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            string[] zh3 = { "礼拜日", "礼拜一", "礼拜二", "礼拜三", "礼拜四", "礼拜五", "礼拜六" };
            for (int i = 0; i < 7; i++)
            {
                if (string.Equals(t, enFull[i], StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t, enAb[i], StringComparison.OrdinalIgnoreCase)
                    || t == zh1[i] || t == zh2[i] || t == zh3[i])
                {
                    dow = (DayOfWeek)i;
                    return true;
                }
            }
            return false;
        }

        private static TimeParseResult Make(DateTimeOffset instant, string kind, string note)
        {
            return new TimeParseResult { Instant = instant, Kind = kind, Note = note };
        }
    }
}
