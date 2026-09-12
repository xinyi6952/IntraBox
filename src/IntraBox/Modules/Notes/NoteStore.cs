using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Markup;
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
        private static bool _writeBlocked;
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
                if (!item.IsFolder)
                    Directory.CreateDirectory(DataPaths.NoteItemDir(item.Uid));
                _items.Insert(0, Clone(item));
                SchedulePersistLocked();
                return Clone(item);
            }
        }

        public static void UpsertMeta(NoteItem item)
        {
            if (item == null) return;
            if (item.ReadOnly) return;
            EnsureLoaded();
            lock (_sync)
            {
                int i = IndexOfLocked(item.Uid);
                if (i >= 0 && _items[i].ReadOnly) return;
                Normalize(item);
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
                if (it == null || it.ReadOnly) return;
                it.Pinned = pinned;
                it.UpdatedAt = DateTime.Now;
                SchedulePersistLocked();
            }
        }

        public static void SetReadOnly(string uid, bool readOnly)
        {
            EnsureLoaded();
            lock (_sync)
            {
                var it = FindLocked(uid);
                if (it == null) return;
                it.ReadOnly = readOnly;
                SchedulePersistLocked();
            }
        }

        public static void Delete(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            EnsureLoaded();
            lock (_sync)
            {
                if (SubtreeHasLockLocked(uid)) return;
                var ids = NoteTree.SubtreeUids(_items, uid);
                for (int k = 0; k < ids.Count; k++)
                {
                    int i = IndexOfLocked(ids[k]);
                    if (i < 0) continue;
                    bool folder = _items[i].IsFolder;
                    _items.RemoveAt(i);
                    if (folder) continue;
                    try
                    {
                        string dir = DataPaths.NoteItemDir(ids[k]);
                        if (Directory.Exists(dir))
                            Directory.Delete(dir, true);
                    }
                    catch { }
                }
                SchedulePersistLocked();
            }
        }

        public static bool TryMove(string uid, string newParentUid, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(uid))
            {
                error = "笔记不存在。";
                return false;
            }
            EnsureLoaded();
            lock (_sync)
            {
                var it = FindLocked(uid);
                if (it == null)
                {
                    error = "笔记不存在。";
                    return false;
                }
                if (it.ReadOnly)
                {
                    error = "已锁定，不能移动。";
                    return false;
                }
                string p = newParentUid ?? "";
                if (p.Length > 0)
                {
                    var parent = FindLocked(p);
                    if (parent != null && parent.ReadOnly)
                    {
                        error = "目标目录已锁定，不能移入。";
                        return false;
                    }
                }
                if (!NoteTree.CanMoveNote(_items, uid, newParentUid, out error))
                    return false;
                if ((it.ParentUid ?? "") == p)
                    return true;
                it.ParentUid = p;
                it.Title = NoteTree.UniqueSiblingName(_items, p, it.Title, it.Uid);
                it.UpdatedAt = DateTime.Now;
                SchedulePersistLocked();
                return true;
            }
        }

        public static string TryDuplicate(string uid, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(uid))
            {
                error = "条目不存在。";
                return null;
            }
            EnsureLoaded();
            lock (_sync)
            {
                var srcUids = new List<string>();
                var copies = NoteTree.DuplicateSubtree(_items, uid, srcUids);
                if (copies.Count == 0)
                {
                    error = "条目不存在。";
                    return null;
                }
                if (_items.Count + copies.Count > NoteTree.MaxNodes)
                {
                    error = "笔记数量已达上限。";
                    return null;
                }
                for (int i = 0; i < copies.Count; i++)
                {
                    var src = copies[i];
                    Normalize(src);
                    if (!src.IsFolder && i < srcUids.Count)
                    {
                        try { CopyNoteFiles(srcUids[i], src.Uid); }
                        catch { }
                    }
                    _items.Insert(0, Clone(src));
                }
                SchedulePersistLocked();
                return copies[0].Uid;
            }
        }

        public static int CountDescendants(string uid)
        {
            EnsureLoaded();
            lock (_sync)
            {
                return NoteTree.CountDescendants(_items, uid);
            }
        }

        public static bool IsLocked(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            var it = GetByUid(uid);
            return it != null && it.ReadOnly;
        }

        public static bool SubtreeHasLock(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            EnsureLoaded();
            lock (_sync)
            {
                return SubtreeHasLockLocked(uid);
            }
        }

        private static bool SubtreeHasLockLocked(string uid)
        {
            var ids = NoteTree.SubtreeUids(_items, uid);
            for (int i = 0; i < ids.Count; i++)
            {
                var it = FindLocked(ids[i]);
                if (it != null && it.ReadOnly) return true;
            }
            return false;
        }

        public static bool CanAddUnder(string parentUid)
        {
            EnsureLoaded();
            lock (_sync)
            {
                return NoteTree.CanAddChild(_items, parentUid);
            }
        }

        public static string UniqueTitle(string parentUid, string baseTitle, string exceptUid)
        {
            EnsureLoaded();
            lock (_sync)
            {
                return NoteTree.UniqueSiblingName(_items, parentUid, baseTitle, exceptUid);
            }
        }

        public static void Flush()
        {
            lock (_sync)
            {
                if (!_loaded) return;
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
            _writeBlocked = false;
            string path = DataPaths.NotesIndexJson;
            bool loadFailed = false;
            try
            {
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
                loadFailed = true;
            }
            if (loadFailed)
            {
                if (!DataFileGuard.TryQuarantine(path))
                    _writeBlocked = true;
                else
                {
                    _sampleSeeded = true;
                    _sampleRev = CurrentSampleRev;
                    try { WriteLocked(); }
                    catch { }
                }
            }
            else if (!_sampleSeeded)
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
            _items = NoteTree.Sanitize(_items);
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
            TryRefreshRichSampleLocked();
            TryRefreshMdSampleLocked();
        }

        private static void TryRefreshRichSampleLocked()
        {
            var rich = FindLocked(SampleRichUid);
            if (rich == null || rich.ReadOnly) return;
            if (HasAttachedFiles(DataPaths.NoteFilesDir(rich.Uid))) return;
            string current;
            string stamp;
            if (!TryReadNoteXaml(rich.Uid, out current, out stamp)) return;
            var stockDoc = FlowDocStamp.Roundtrip(NoteRichText.CreateSampleDocument());
            if (stockDoc == null) return;
            string stockPlain = NoteRichText.ToPlain(stockDoc);
            string stockStamp = FlowDocStamp.From(stockDoc);
            if (!SampleGuard.ShouldRefresh(rich.Title, current, stockPlain, stamp, stockStamp)) return;
            rich.Title = "【示例】本周联调纪要（普通笔记）";
            rich.UpdatedAt = DateTime.Now;
            try
            {
                Directory.CreateDirectory(DataPaths.NoteItemDir(rich.Uid));
                NoteRichText.SaveDocument(NoteRichText.CreateSampleDocument(), rich.Uid);
            }
            catch { }
        }

        private static void TryRefreshMdSampleLocked()
        {
            var md = FindLocked(SampleMdUid);
            if (md == null || md.ReadOnly) return;
            if (HasAttachedFiles(DataPaths.NoteFilesDir(md.Uid))) return;
            string current;
            if (!TryReadTextFile(DataPaths.NoteBodyMdPath(md.Uid), out current)) return;
            if (!SampleGuard.ShouldRefresh(md.Title, current, NoteMarkdown.SampleText)) return;
            md.Title = "【示例】排障手册（Markdown）";
            md.UpdatedAt = DateTime.Now;
            try
            {
                Directory.CreateDirectory(DataPaths.NoteItemDir(md.Uid));
                File.WriteAllText(DataPaths.NoteBodyMdPath(md.Uid), NoteMarkdown.SampleText, Encoding.UTF8);
            }
            catch { }
        }

        private static bool TryReadNoteXaml(string uid, out string plain, out string stamp)
        {
            plain = null;
            stamp = null;
            string path = DataPaths.NoteBodyXamlPath(uid);
            if (!File.Exists(path)) return true;
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var doc = XamlReader.Load(fs) as FlowDocument;
                    plain = NoteRichText.ToPlain(doc);
                    stamp = FlowDocStamp.From(doc);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadTextFile(string path, out string text)
        {
            text = null;
            if (!File.Exists(path)) return true;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
                return true;
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

        private static void Normalize(NoteItem it)
        {
            var n = NoteTree.Normalize(it);
            if (n == null || it == null) return;
            it.Uid = n.Uid;
            it.Title = n.Title;
            it.Kind = n.Kind;
            it.Type = n.Type;
            it.ParentUid = n.ParentUid;
            it.Pinned = n.Pinned;
            it.ReadOnly = n.ReadOnly;
            it.CreatedAt = n.CreatedAt == default(DateTime) ? DateTime.Now : n.CreatedAt;
            it.UpdatedAt = n.UpdatedAt == default(DateTime) ? it.CreatedAt : n.UpdatedAt;
        }

        private static void CopyNoteFiles(string srcUid, string destUid)
        {
            if (string.IsNullOrEmpty(srcUid) || string.IsNullOrEmpty(destUid)) return;
            string src = DataPaths.NoteItemDir(srcUid);
            string dest = DataPaths.NoteItemDir(destUid);
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dest);
            string[] files = Directory.GetFiles(src);
            for (int i = 0; i < files.Length; i++)
                File.Copy(files[i], Path.Combine(dest, Path.GetFileName(files[i])), true);
            string[] dirs = Directory.GetDirectories(src);
            for (int i = 0; i < dirs.Length; i++)
                CopyNoteFilesDeep(dirs[i], Path.Combine(dest, Path.GetFileName(dirs[i])));
        }

        private static void CopyNoteFilesDeep(string src, string dest)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dest);
            string[] files = Directory.GetFiles(src);
            for (int i = 0; i < files.Length; i++)
                File.Copy(files[i], Path.Combine(dest, Path.GetFileName(files[i])), true);
            string[] dirs = Directory.GetDirectories(src);
            for (int i = 0; i < dirs.Length; i++)
                CopyNoteFilesDeep(dirs[i], Path.Combine(dest, Path.GetFileName(dirs[i])));
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
            if (_writeBlocked) return;
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
                Type = s.Type,
                ParentUid = s.ParentUid ?? "",
                Pinned = s.Pinned,
                ReadOnly = s.ReadOnly,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            };
        }
    }
}
