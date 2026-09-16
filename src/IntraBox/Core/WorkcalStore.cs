using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace IntraBox.Core
{
    public sealed class WorkcalFile
    {
        public int FormatVersion { get; set; }
        public List<string> Off { get; set; }
        public List<string> Work { get; set; }
    }

    /// <summary>用户放假/补班覆盖。内置表进程内缓存；用户表读数据根 workcal.json。</summary>
    public static class WorkcalStore
    {
        public const int FormatVersion = 1;

        private static readonly object Sync = new object();
        private static HashSet<string> _builtinOff;
        private static HashSet<string> _builtinWork;
        private static HashSet<string> _userOff = new HashSet<string>(StringComparer.Ordinal);
        private static HashSet<string> _userWork = new HashSet<string>(StringComparer.Ordinal);
        private static bool _loaded;
        private static bool _writeBlocked;

        public static HashSet<string> UserOff()
        {
            EnsureLoaded();
            lock (Sync) { return new HashSet<string>(_userOff, StringComparer.Ordinal); }
        }

        public static HashSet<string> UserWork()
        {
            EnsureLoaded();
            lock (Sync) { return new HashSet<string>(_userWork, StringComparer.Ordinal); }
        }

        public static bool BuiltinOff(string ymd)
        {
            EnsureBuiltin();
            return _builtinOff.Contains(ymd);
        }

        public static bool BuiltinWork(string ymd)
        {
            EnsureBuiltin();
            return _builtinWork.Contains(ymd);
        }

        public static void Reload()
        {
            lock (Sync)
            {
                _loaded = false;
                LoadLocked();
            }
        }

        public static bool SetMark(DateTime date, WorkcalMark mark, out string error)
        {
            error = null;
            string k = WorkdayHelper.Key(date);
            EnsureLoaded();
            lock (Sync)
            {
                if (_writeBlocked)
                {
                    error = "放假表损坏且无法隔离，暂不能保存";
                    return false;
                }
                _userOff.Remove(k);
                _userWork.Remove(k);
                if (mark == WorkcalMark.Off) _userOff.Add(k);
                else if (mark == WorkcalMark.Work) _userWork.Add(k);
                try
                {
                    WriteLocked();
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.RootMessage();
                    return false;
                }
            }
        }

        private static void EnsureBuiltin()
        {
            if (_builtinOff != null) return;
            lock (Sync)
            {
                if (_builtinOff != null) return;
                var off = new HashSet<string>(StringComparer.Ordinal);
                var work = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < WorkdayCnHolidays.Years.Length; i++)
                    WorkdayCnHolidays.Fill(WorkdayCnHolidays.Years[i], off, work);
                _builtinOff = off;
                _builtinWork = work;
            }
        }

        private static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (!_loaded) LoadLocked();
            }
        }

        private static void LoadLocked()
        {
            EnsureBuiltin();
            _userOff = new HashSet<string>(StringComparer.Ordinal);
            _userWork = new HashSet<string>(StringComparer.Ordinal);
            _writeBlocked = false;
            _loaded = true;
            string path = DataPaths.WorkCalJson;
            if (!File.Exists(path)) return;
            try
            {
                var file = JsonConvert.DeserializeObject<WorkcalFile>(File.ReadAllText(path, Encoding.UTF8));
                if (file == null) return;
                AddDates(_userOff, file.Off);
                AddDates(_userWork, file.Work);
            }
            catch
            {
                _userOff.Clear();
                _userWork.Clear();
                if (!DataFileGuard.TryQuarantine(path))
                    _writeBlocked = true;
            }
        }

        private static void AddDates(HashSet<string> set, List<string> src)
        {
            if (src == null) return;
            for (int i = 0; i < src.Count; i++)
            {
                string ymd;
                if (TryParseYmd(src[i], out ymd))
                    set.Add(ymd);
            }
        }

        /// <summary>只认 yyyy-MM-dd（不变文化），避免 CurrentCulture 把 3/4/2026 等误解析。</summary>
        public static bool TryParseYmd(string text, out string ymd)
        {
            ymd = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            DateTime d;
            if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return false;
            ymd = WorkdayHelper.Key(d);
            return true;
        }

        private static void WriteLocked()
        {
            if (_writeBlocked) return;
            var file = new WorkcalFile
            {
                FormatVersion = FormatVersion,
                Off = ToList(_userOff),
                Work = ToList(_userWork)
            };
            string json = JsonConvert.SerializeObject(file, Formatting.Indented);
            Directory.CreateDirectory(DataPaths.Root);
            File.WriteAllText(DataPaths.WorkCalJson, json, new UTF8Encoding(false));
        }

        private static List<string> ToList(HashSet<string> set)
        {
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}
