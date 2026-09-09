using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Markup;
using ClosedXML.Excel;
using IntraBox.Core;
using Newtonsoft.Json;

namespace IntraBox.Modules.Todo
{
    public static class TodoStore
    {
        public const int IndexVersion = 1;

        private static readonly object _sync = new object();
        private static List<TodoItem> _items = new List<TodoItem>();
        private static Timer _diskTimer;
        private static int _inflight;
        private static bool _flushing;
        private static bool _loaded;
        private static bool _sampleSeeded;
        private static int _sampleRev;
        private const int CurrentSampleRev = 3;

        public static void Reload()
        {
            lock (_sync)
            {
                LoadLocked();
            }
        }

        public static List<TodoItem> Snapshot()
        {
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                var copy = new List<TodoItem>(_items.Count);
                for (int i = 0; i < _items.Count; i++)
                    copy.Add(Clone(_items[i]));
                return copy;
            }
        }

        public static TodoItem GetByUid(string uid)
        {
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                var it = FindLocked(uid);
                return it == null ? null : Clone(it);
            }
        }

        public static bool IdExists(string id, string exceptUid)
        {
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                for (int i = 0; i < _items.Count; i++)
                {
                    if (exceptUid != null && _items[i].Uid == exceptUid) continue;
                    if (string.Equals(_items[i].Id, id, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }

        public static string NextAutoId()
        {
            string prefix = "T-" + DateTime.Now.ToString("yyyyMMdd") + "-";
            int max = 0;
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                for (int i = 0; i < _items.Count; i++)
                {
                    string id = _items[i].Id;
                    if (id == null || !id.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    string rest = id.Substring(prefix.Length);
                    int n;
                    if (int.TryParse(rest, out n) && n > max) max = n;
                }
            }
            return prefix + (max + 1).ToString("000");
        }

        public static TodoItem AddNew(string id)
        {
            var item = TodoItem.CreateNew(id);
            Insert(item);
            return Clone(item);
        }

        public static void Insert(TodoItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Uid)) return;
            Directory.CreateDirectory(DataPaths.TodoItemDir(item.Uid));
            item.UpdatedAt = DateTime.Now;
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                int idx = IndexOfLocked(item.Uid);
                if (idx >= 0) _items[idx] = Clone(item);
                else _items.Insert(0, Clone(item));
                SchedulePersistLocked();
            }
        }

        public static void Upsert(TodoItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Uid)) return;
            if (item.ReadOnly) return;
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                int idx = IndexOfLocked(item.Uid);
                if (idx >= 0 && _items[idx].ReadOnly) return;
                item.UpdatedAt = DateTime.Now;
                if (idx >= 0) _items[idx] = Clone(item);
                else _items.Insert(0, Clone(item));
                SchedulePersistLocked();
            }
        }

        public static void Delete(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                int idx = IndexOfLocked(uid);
                if (idx < 0) return;
                if (_items[idx].ReadOnly) return;
                _items.RemoveAt(idx);
                SchedulePersistLocked();
            }
            try
            {
                string dir = DataPaths.TodoItemDir(uid);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch { }
        }

        public static void SetReadOnly(string uid, bool readOnly)
        {
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                var it = FindLocked(uid);
                if (it == null) return;
                it.ReadOnly = readOnly;
                SchedulePersistLocked();
            }
        }

        public static void SetCompleted(string uid, bool completed)
        {
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                var it = FindLocked(uid);
                if (it == null || it.ReadOnly) return;
                it.Completed = completed;
                it.UpdatedAt = DateTime.Now;
                if (completed)
                {
                    it.CompletedAt = DateTime.Now;
                    it.LastRemindedAt = DateTime.Now;
                }
                else
                {
                    it.CompletedAt = null;
                }
                SchedulePersistLocked();
            }
        }

        /// <summary>记下本次弹出，返回当天已弹次数（含本次）。</summary>
        public static int MarkReminded(string uid, DateTime when)
        {
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                var it = FindLocked(uid);
                if (it == null) return 0;
                TodoRemindRepeat.RegisterFire(it, when);
                SchedulePersistLocked();
                return TodoRemindRepeat.FiredToday(it, when);
            }
        }

        /// <summary>今日不再提醒：不改频次，次日仍按原规则弹。</summary>
        public static void SetMuteRemindToday(string uid, DateTime now)
        {
            if (string.IsNullOrEmpty(uid)) return;
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
                var it = FindLocked(uid);
                if (it == null) return;
                it.MuteRemindOn = now.Date;
                it.UpdatedAt = DateTime.Now;
                SchedulePersistLocked();
            }
        }

        public static void Flush()
        {
            lock (_sync)
            {
                _flushing = true;
                if (_diskTimer != null)
                {
                    _diskTimer.Dispose();
                    _diskTimer = null;
                }
                WriteLocked();
                _flushing = false;
            }
        }

        public static string ExportXlsx(string xlsxPath, List<TodoItem> items)
        {
            if (items == null) items = Snapshot();
            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.AddWorksheet("任务计划");
                    string[] headers =
                    {
                        "任务ID", "状态", "优先级", "创建时间", "修改时间",
                        "计划完成", "完成时间", "提醒", "任务标题", "任务来源", "任务描述", "图片"
                    };
                    for (int c = 0; c < headers.Length; c++)
                    {
                        var head = ws.Cell(1, c + 1);
                        WriteText(head, headers[c]);
                        head.Style.Font.Bold = true;
                    }
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        string plain = "";
                        System.Windows.Documents.FlowDocument doc = null;
                        try
                        {
                            var path = DataPaths.TodoDetailsPath(it.Uid);
                            if (File.Exists(path))
                            {
                                using (var fs = File.OpenRead(path))
                                    doc = System.Windows.Markup.XamlReader.Load(fs) as System.Windows.Documents.FlowDocument;
                                plain = TodoRichText.ToPlain(doc);
                            }
                        }
                        catch { }
                        int r = i + 2;
                        WriteText(ws.Cell(r, 1), it.Id);
                        WriteText(ws.Cell(r, 2), it.Completed ? "已办" : "待办");
                        WriteText(ws.Cell(r, 3), TodoPriority.Label(it.Priority));
                        WriteText(ws.Cell(r, 4), it.CreatedAt == default(DateTime) ? "" : it.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
                        WriteText(ws.Cell(r, 5), it.UpdatedAt == default(DateTime) ? "" : it.UpdatedAt.ToString("yyyy-MM-dd HH:mm"));
                        var dueCell = ws.Cell(r, 6);
                        WriteText(dueCell, it.DueAt.HasValue ? it.DueAt.Value.ToString("yyyy-MM-dd HH:mm") : "");
                        if (TodoDue.IsDatePast(it.DueAt))
                            dueCell.Style.Font.FontColor = XLColor.FromArgb(199, 34, 34);
                        WriteText(ws.Cell(r, 7), it.CompletedAt.HasValue ? it.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm") : "");
                        WriteText(ws.Cell(r, 8), it.Completed ? "" : RemindSummary(it));
                        WriteText(ws.Cell(r, 9), TodoRichText.ExtractTitle(plain));
                        WriteText(ws.Cell(r, 10), TodoRichText.ExtractField(plain, "任务来源"));
                        var desc = ws.Cell(r, 11);
                        WriteText(desc, TodoRichText.ExtractField(plain, "任务描述"));
                        desc.Style.Alignment.WrapText = true;
                        desc.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                        EmbedTaskPictures(ws, r, it.Uid, doc);
                    }
                    ws.SheetView.FreezeRows(1);
                    double[] widths = { 22, 10, 10, 20, 20, 20, 20, 16, 28, 24, 48, 22 };
                    for (int c = 0; c < widths.Length; c++)
                        ws.Column(c + 1).Width = widths[c];
                    wb.SaveAs(xlsxPath);
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static void WriteText(IXLCell cell, string text)
        {
            if (text == null) text = "";
            cell.Style.NumberFormat.Format = "@";
            cell.SetValue(text);
            cell.SetDataType(XLDataType.Text);
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        private static void EmbedTaskPictures(IXLWorksheet ws, int row, string uid, System.Windows.Documents.FlowDocument doc)
        {
            var names = TodoRichText.ListImageFileNames(doc);
            string dir = DataPaths.TodoFilesDir(uid);
            if (names.Count == 0 && Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir);
                for (int i = 0; i < files.Length; i++)
                {
                    if (TodoRichText.IsAllowedImageExt(Path.GetExtension(files[i])))
                        names.Add(Path.GetFileName(files[i]));
                }
            }
            double rowH = 18;
            for (int p = 0; p < names.Count; p++)
            {
                string fp = Path.Combine(dir, names[p]);
                if (!File.Exists(fp)) continue;
                try
                {
                    var pic = ws.AddPicture(fp, "p" + row + "_" + (p + 1));
                    pic.MoveTo(ws.Cell(row, 12 + p));
                    int ow = pic.OriginalWidth < 1 ? 120 : pic.OriginalWidth;
                    int oh = pic.OriginalHeight < 1 ? 90 : pic.OriginalHeight;
                    double sc = Math.Min(140.0 / ow, 100.0 / oh);
                    if (sc > 1) sc = 1;
                    int w = Math.Max(1, (int)(ow * sc));
                    int h = Math.Max(1, (int)(oh * sc));
                    pic.WithSize(w, h);
                    if (h + 10 > rowH) rowH = h + 10;
                    ws.Column(12 + p).Width = 22;
                }
                catch { }
            }
            if (rowH > 18)
                ws.Row(row).Height = rowH;
        }

        public static string RemindSummary(TodoItem it)
        {
            if (it == null || it.Completed) return "";
            string hm = it.RemindHour.ToString("00") + ":" + it.RemindMinute.ToString("00");
            int times = TodoRemindRepeat.ClampTimes(it.RemindTimes);
            int every = TodoRemindRepeat.ClampIntervalMin(it.RemindIntervalMin);
            string extra = times > 1 ? " · " + times + "次/" + every + "分" : "";
            switch (it.RemindKind)
            {
                case TodoRemindKind.Off: return "关闭";
                case TodoRemindKind.Weekdays: return "工作日 " + hm + extra;
                case TodoRemindKind.Weekly:
                    return "每周" + WeekdayName(it.RemindWeekday) + " " + hm + extra;
                case TodoRemindKind.EveryNDays:
                    return "每" + Math.Max(2, it.RemindNDays) + "天 " + hm + extra;
                case TodoRemindKind.DueDay: return "计划完成当天 " + hm + extra;
                default: return "每天 " + hm + extra;
            }
        }

        public static string WeekdayName(int d)
        {
            switch (d)
            {
                case 0: return "日";
                case 1: return "一";
                case 2: return "二";
                case 3: return "三";
                case 4: return "四";
                case 5: return "五";
                default: return "六";
            }
        }

        /// <summary>反序列化 index.json 并补缺省（不读写当前数据根）。旧条目缺次数/间隔时按 3 次/10 分。</summary>
        public static List<TodoItem> ParseIndexJson(string json)
        {
            var result = new List<TodoItem>();
            if (string.IsNullOrEmpty(json)) return result;
            try
            {
                var file = JsonConvert.DeserializeObject<TodoIndexFile>(json);
                if (file == null || file.Items == null) return result;
                for (int i = 0; i < file.Items.Count; i++)
                {
                    if (file.Items[i] == null) continue;
                    NormalizeItem(file.Items[i]);
                    result.Add(file.Items[i]);
                }
            }
            catch
            {
                return new List<TodoItem>();
            }
            return result;
        }

        private static void LoadLocked()
        {
            _items = new List<TodoItem>();
            _loaded = true;
            _sampleSeeded = false;
            _sampleRev = 0;
            try
            {
                Directory.CreateDirectory(DataPaths.TodosDir);
                string path = DataPaths.TodosIndexJson;
                if (File.Exists(path))
                {
                    var file = JsonConvert.DeserializeObject<TodoIndexFile>(File.ReadAllText(path, Encoding.UTF8));
                    if (file != null)
                    {
                        _sampleSeeded = file.SampleSeeded;
                        _sampleRev = file.SampleRev;
                        if (file.Items != null)
                        {
                            for (int i = 0; i < file.Items.Count; i++)
                            {
                                if (file.Items[i] == null) continue;
                                NormalizeItem(file.Items[i]);
                                _items.Add(file.Items[i]);
                            }
                        }
                    }
                }
            }
            catch
            {
                _items = new List<TodoItem>();
                _sampleSeeded = false;
                _sampleRev = 0;
            }
            if (!_sampleSeeded)
            {
                SeedSampleLocked();
                _sampleSeeded = true;
                _sampleRev = CurrentSampleRev;
                try { WriteLocked(); }
                catch { }
            }
            else if (_sampleRev < CurrentSampleRev)
            {
                RefreshSampleLocked();
                _sampleRev = CurrentSampleRev;
                try { WriteLocked(); }
                catch { }
            }
        }

        private const string SampleUid = "todoexample000000000000000000001";
        private const string SampleId = "T-EXAMPLE-001";

        private static void SeedSampleLocked()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Uid == SampleUid || string.Equals(_items[i].Id, SampleId, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            var now = DateTime.Now;
            var item = new TodoItem
            {
                Uid = SampleUid,
                CreatedAt = now
            };
            ApplySampleFields(item, now);
            NormalizeItem(item);
            try
            {
                Directory.CreateDirectory(DataPaths.TodoItemDir(item.Uid));
                TodoRichText.SaveDocument(TodoRichText.CreateSampleDocument(), item.Uid);
            }
            catch { }
            _items.Insert(0, item);
        }

        private static void RefreshSampleLocked()
        {
            TodoItem item = null;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Uid == SampleUid || string.Equals(_items[i].Id, SampleId, StringComparison.OrdinalIgnoreCase))
                {
                    item = _items[i];
                    break;
                }
            }
            if (item == null) return;
            if (item.Completed) return;
            if (item.ReadOnly) return;
            if (HasAttachedFiles(DataPaths.TodoFilesDir(item.Uid))) return;
            string current;
            string stamp;
            if (!TryReadTodoXaml(item.Uid, out current, out stamp)) return;
            var stockDoc = FlowDocStamp.Roundtrip(TodoRichText.CreateSampleDocument());
            if (stockDoc == null) return;
            string stockPlain = TodoRichText.ToPlain(stockDoc);
            string stockStamp = FlowDocStamp.From(stockDoc);
            string title = TodoRichText.ExtractTitle(current ?? "");
            if (!SampleGuard.ShouldRefresh(title, current, stockPlain, stamp, stockStamp)) return;
            ApplySampleFields(item, DateTime.Now);
            try
            {
                Directory.CreateDirectory(DataPaths.TodoItemDir(item.Uid));
                TodoRichText.SaveDocument(TodoRichText.CreateSampleDocument(), item.Uid);
            }
            catch { }
        }

        private static bool TryReadTodoXaml(string uid, out string plain, out string stamp)
        {
            plain = null;
            stamp = null;
            string path = DataPaths.TodoDetailsPath(uid);
            if (!File.Exists(path)) return true;
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var doc = XamlReader.Load(fs) as FlowDocument;
                    plain = TodoRichText.ToPlain(doc);
                    stamp = FlowDocStamp.From(doc);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool HasAttachedFiles(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplySampleFields(TodoItem item, DateTime now)
        {
            if (item == null) return;
            item.Id = SampleId;
            item.Priority = TodoPriority.High;
            item.DueAt = SampleDueFriday(now);
            item.Completed = false;
            item.CompletedAt = null;
            item.RemindKind = TodoRemindKind.Weekdays;
            item.RemindHour = 15;
            item.RemindMinute = 0;
            item.RemindNDays = 2;
            item.RemindWeekday = 1;
            item.RemindTimes = TodoRemindRepeat.DefaultTimes;
            item.RemindIntervalMin = TodoRemindRepeat.DefaultIntervalMin;
            item.UpdatedAt = now;
        }

        private static DateTime SampleDueFriday(DateTime now)
        {
            int add = ((int)DayOfWeek.Friday - (int)now.DayOfWeek + 7) % 7;
            var due = now.Date.AddDays(add).AddHours(18);
            if (due <= now) due = due.AddDays(7);
            return due;
        }

        private static void NormalizeItem(TodoItem it)
        {
            if (string.IsNullOrEmpty(it.Uid)) it.Uid = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(it.Id)) it.Id = it.Uid;
            if (it.RemindHour < 0 || it.RemindHour > 23) it.RemindHour = 15;
            if (it.RemindMinute < 0 || it.RemindMinute > 59) it.RemindMinute = 0;
            if (it.RemindNDays < 2) it.RemindNDays = 2;
            if (it.RemindWeekday < 0 || it.RemindWeekday > 6) it.RemindWeekday = 1;
            it.RemindTimes = TodoRemindRepeat.ClampTimes(it.RemindTimes);
            it.RemindIntervalMin = TodoRemindRepeat.ClampIntervalMin(it.RemindIntervalMin);
            if (it.CreatedAt == default(DateTime)) it.CreatedAt = DateTime.Now;
            if (it.UpdatedAt == default(DateTime)) it.UpdatedAt = it.CreatedAt;
        }

        private static void SchedulePersistLocked()
        {
            int ms = AppSettings.CurrentHistoryPersistDelayMs();
            if (_diskTimer != null) _diskTimer.Dispose();
            _diskTimer = new Timer(BackgroundPersist, null, ms, Timeout.Infinite);
        }

        private static void BackgroundPersist(object state)
        {
            if (Interlocked.Exchange(ref _inflight, 1) == 1)
            {
                lock (_sync)
                {
                    if (!_flushing)
                        _diskTimer = new Timer(BackgroundPersist, null, 50, Timeout.Infinite);
                }
                return;
            }
            try
            {
                lock (_sync)
                {
                    WriteLocked();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _inflight, 0);
            }
        }

        private static void WriteLocked()
        {
            Directory.CreateDirectory(DataPaths.TodosDir);
            var file = new TodoIndexFile
            {
                Version = IndexVersion,
                SampleSeeded = _sampleSeeded,
                SampleRev = _sampleRev,
                Items = _items
            };
            string json = JsonConvert.SerializeObject(file, Formatting.Indented);
            string path = DataPaths.TodosIndexJson;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);
            File.Copy(tmp, path, true);
            try { File.Delete(tmp); } catch { }
        }

        private static TodoItem FindLocked(string uid)
        {
            int i = IndexOfLocked(uid);
            return i < 0 ? null : _items[i];
        }

        private static bool IdExistsUnlocked(string id, string exceptUid)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (exceptUid != null && _items[i].Uid == exceptUid) continue;
                if (string.Equals(_items[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static int IndexOfLocked(string uid)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Uid == uid) return i;
            }
            return -1;
        }

        private static TodoItem Clone(TodoItem s)
        {
            return new TodoItem
            {
                Uid = s.Uid,
                Id = s.Id,
                Priority = s.Priority,
                DueAt = s.DueAt,
                Completed = s.Completed,
                RemindKind = s.RemindKind,
                RemindHour = s.RemindHour,
                RemindMinute = s.RemindMinute,
                RemindNDays = s.RemindNDays,
                RemindWeekday = s.RemindWeekday,
                RemindTimes = s.RemindTimes,
                RemindIntervalMin = s.RemindIntervalMin,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
                LastRemindedAt = s.LastRemindedAt,
                RemindFiredOn = s.RemindFiredOn,
                RemindFiredCount = s.RemindFiredCount,
                MuteRemindOn = s.MuteRemindOn,
                CompletedAt = s.CompletedAt,
                ReadOnly = s.ReadOnly
            };
        }
    }
}
