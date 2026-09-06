using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using IntraBox.Core;
using Newtonsoft.Json;

namespace IntraBox.Modules.Notes
{
    public static class NoteStore
    {
        public const int IndexVersion = 1;

        private static readonly object _sync = new object();
        private static List<NoteItem> _items = new List<NoteItem>();
        private static Timer _diskTimer;
        private static int _inflight;
        private static bool _flushing;
        private static bool _loaded;
        private static bool _sampleSeeded;
        private static int _sampleRev;
        private const int CurrentSampleRev = 2;

        public static void Reload()
        {
            lock (_sync)
            {
                LoadLocked();
            }
        }

        public static List<NoteItem> Snapshot()
        {
            EnsureLoaded();
            lock (_sync)
            {
                var list = new List<NoteItem>(_items.Count);
                for (int i = 0; i < _items.Count; i++)
                    list.Add(Clone(_items[i]));
                return list;
            }
        }

        public static NoteItem GetByUid(string uid)
        {
            EnsureLoaded();
            lock (_sync)
            {
                var it = FindLocked(uid);
                return it == null ? null : Clone(it);
            }
        }

        public static NoteItem Add(NoteItem item)
        {
            if (item == null) return null;
            EnsureLoaded();
            lock (_sync)
            {
                Normalize(item);
                Directory.CreateDirectory(DataPaths.NoteItemDir(item.Uid));
                _items.Insert(0, Clone(item));
                SchedulePersistLocked();
                return Clone(item);
            }
        }

        public static void UpsertMeta(NoteItem item)
        {
            if (item == null) return;
            EnsureLoaded();
            lock (_sync)
            {
                Normalize(item);
                int i = IndexOfLocked(item.Uid);
                if (i < 0)
                    _items.Insert(0, Clone(item));
                else
                    _items[i] = Clone(item);
                SchedulePersistLocked();
            }
        }

        public static void SetPinned(string uid, bool pinned)
        {
            EnsureLoaded();
            lock (_sync)
            {
                var it = FindLocked(uid);
                if (it == null) return;
                it.Pinned = pinned;
                it.UpdatedAt = DateTime.Now;
                SchedulePersistLocked();
            }
        }

        public static void Delete(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            EnsureLoaded();
            lock (_sync)
            {
                int i = IndexOfLocked(uid);
                if (i < 0) return;
                _items.RemoveAt(i);
                try
                {
                    string dir = DataPaths.NoteItemDir(uid);
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch { }
                SchedulePersistLocked();
            }
        }

        public static string UniqueTitle(int kind, string exceptUid)
        {
            string baseTitle = NoteKind.DefaultTitle(kind);
            EnsureLoaded();
            lock (_sync)
            {
                if (!TitleExistsLocked(baseTitle, exceptUid))
                    return baseTitle;
                int n = 2;
                while (TitleExistsLocked(baseTitle + " " + n, exceptUid))
                    n++;
                return baseTitle + " " + n;
            }
        }

        public static void Flush()
        {
            EnsureLoaded();
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

        private static void EnsureLoaded()
        {
            lock (_sync)
            {
                if (!_loaded)
                {
                    LoadLocked();
                    _loaded = true;
                }
            }
        }

        private static void LoadLocked()
        {
            _items = new List<NoteItem>();
            _sampleSeeded = false;
            _sampleRev = 0;
            try
            {
                string path = DataPaths.NotesIndexJson;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var file = JsonConvert.DeserializeObject<NoteIndexFile>(json);
                    if (file != null)
                    {
                        _sampleSeeded = file.SampleSeeded;
                        _sampleRev = file.SampleRev;
                        if (file.Items != null)
                        {
                            for (int i = 0; i < file.Items.Count; i++)
                            {
                                if (file.Items[i] == null) continue;
                                Normalize(file.Items[i]);
                                _items.Add(file.Items[i]);
                            }
                        }
                    }
                }
            }
            catch
            {
                _items = new List<NoteItem>();
                _sampleSeeded = false;
                _sampleRev = 0;
            }
            if (!_sampleSeeded)
            {
                SeedSamplesLocked();
                _sampleSeeded = true;
                _sampleRev = CurrentSampleRev;
                try { WriteLocked(); }
                catch { }
            }
            else if (_sampleRev < CurrentSampleRev)
            {
                RefreshSamplesLocked();
                _sampleRev = CurrentSampleRev;
                try { WriteLocked(); }
                catch { }
            }
        }

        private const string SampleRichUid = "noteexample000000000000000000001";
        private const string SampleMdUid = "noteexample000000000000000000002";

        private static void SeedSamplesLocked()
        {
            var now = DateTime.Now;
            if (FindLocked(SampleRichUid) == null)
            {
                var rich = new NoteItem
                {
                    Uid = SampleRichUid,
                    Title = "【示例】本周联调纪要（普通笔记）",
                    Kind = NoteKind.Rich,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                Normalize(rich);
                try
                {
                    Directory.CreateDirectory(DataPaths.NoteItemDir(rich.Uid));
                    NoteRichText.SaveDocument(NoteRichText.CreateSampleDocument(), rich.Uid);
                }
                catch { }
                _items.Insert(0, rich);
            }
            if (FindLocked(SampleMdUid) == null)
            {
                var md = new NoteItem
                {
                    Uid = SampleMdUid,
                    Title = "【示例】排障手册（Markdown）",
                    Kind = NoteKind.Markdown,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                Normalize(md);
                try
                {
                    Directory.CreateDirectory(DataPaths.NoteItemDir(md.Uid));
                    File.WriteAllText(DataPaths.NoteBodyMdPath(md.Uid), NoteMarkdown.SampleText, Encoding.UTF8);
                }
                catch { }
                _items.Insert(0, md);
            }
        }

        private static void RefreshSamplesLocked()
        {
            var rich = FindLocked(SampleRichUid);
            if (rich != null)
            {
                rich.Title = "【示例】本周联调纪要（普通笔记）";
                rich.UpdatedAt = DateTime.Now;
                try
                {
                    Directory.CreateDirectory(DataPaths.NoteItemDir(rich.Uid));
                    NoteRichText.SaveDocument(NoteRichText.CreateSampleDocument(), rich.Uid);
                }
                catch { }
            }
            var md = FindLocked(SampleMdUid);
            if (md != null)
            {
                md.Title = "【示例】排障手册（Markdown）";
                md.UpdatedAt = DateTime.Now;
                try
                {
                    Directory.CreateDirectory(DataPaths.NoteItemDir(md.Uid));
                    File.WriteAllText(DataPaths.NoteBodyMdPath(md.Uid), NoteMarkdown.SampleText, Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void Normalize(NoteItem it)
        {
            if (it == null) return;
            if (string.IsNullOrEmpty(it.Uid)) it.Uid = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(it.Title)) it.Title = NoteKind.DefaultTitle(it.Kind);
            if (it.Kind != NoteKind.Markdown) it.Kind = NoteKind.Rich;
            if (it.CreatedAt == default(DateTime)) it.CreatedAt = DateTime.Now;
            if (it.UpdatedAt == default(DateTime)) it.UpdatedAt = it.CreatedAt;
        }

        private static bool TitleExistsLocked(string title, string exceptUid)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (exceptUid != null && _items[i].Uid == exceptUid) continue;
                if (string.Equals(_items[i].Title, title, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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
            Directory.CreateDirectory(DataPaths.NotesDir);
            var file = new NoteIndexFile
            {
                Version = IndexVersion,
                SampleSeeded = _sampleSeeded,
                SampleRev = _sampleRev,
                Items = _items
            };
            string json = JsonConvert.SerializeObject(file, Formatting.Indented);
            string path = DataPaths.NotesIndexJson;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);
            File.Copy(tmp, path, true);
            try { File.Delete(tmp); } catch { }
        }

        private static NoteItem FindLocked(string uid)
        {
            int i = IndexOfLocked(uid);
            return i < 0 ? null : _items[i];
        }

        private static int IndexOfLocked(string uid)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Uid == uid) return i;
            }
            return -1;
        }

        private static NoteItem Clone(NoteItem s)
        {
            if (s == null) return null;
            return new NoteItem
            {
                Uid = s.Uid,
                Title = s.Title,
                Kind = s.Kind,
                Pinned = s.Pinned,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }
}
