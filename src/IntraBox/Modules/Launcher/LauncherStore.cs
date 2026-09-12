using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IntraBox.Core;

namespace IntraBox.Modules.Launcher
{
    /// <summary>读写数据根 launcher.json。节点最多 200，收藏深度 8，最近最多 30。</summary>
    public static class LauncherStore
    {
        public const int MaxRecents = 30;

        private static readonly object _sync = new object();
        private static List<LauncherNode> _nodes = new List<LauncherNode>();
        private static List<string> _recents = new List<string>();
        private static List<string> _systemPinned = new List<string>();
        private static bool _loaded;
        /// <summary>原文件损坏且无法挪走时禁止 Flush，避免把用户数据盖成空表。</summary>
        private static bool _writeBlocked;

        public static void Reload()
        {
            lock (_sync)
            {
                LoadLocked();
            }
        }

        public static void Flush()
        {
            lock (_sync)
            {
                if (!_loaded || _writeBlocked) return;
                try { WriteLocked(); }
                catch { }
            }
        }

        public static List<LauncherNode> SnapshotNodes()
        {
            EnsureLoaded();
            lock (_sync)
            {
                return MergeLocked();
            }
        }

        public static List<LauncherNode> SnapshotFavorites()
        {
            EnsureLoaded();
            lock (_sync)
            {
                var merged = MergeLocked();
                var list = new List<LauncherNode>();
                for (int i = 0; i < merged.Count; i++)
                {
                    if (merged[i] != null && !merged[i].IsCategory)
                        list.Add(merged[i]);
                }
                return list;
            }
        }

        public static List<string> SnapshotRecents()
        {
            EnsureLoaded();
            lock (_sync)
            {
                return new List<string>(_recents);
            }
        }

        public static LauncherNode GetNode(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            EnsureLoaded();
            lock (_sync)
            {
                return LauncherTree.Clone(LauncherTree.Find(MergeLocked(), uid));
            }
        }

        public static LauncherNode GetFavorite(string uid)
        {
            var n = GetNode(uid);
            if (n == null || n.IsCategory) return null;
            return n;
        }

        public static string TryAddCategory(string parentUid, string name, out string error)
        {
            error = "";
            if (LauncherSystemCatalog.IsSystemUid(parentUid))
            {
                error = "不能在系统目录中新增。";
                return null;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "请填写目录名称。";
                return null;
            }
            EnsureLoaded();
            lock (_sync)
            {
                if (!PrepareParentLocked(ref parentUid, true, out error))
                    return null;
                string nm = LauncherTree.UniqueSiblingName(_nodes, parentUid, name.Trim(), null);
                var now = DateTime.Now;
                var node = LauncherTree.NewCategory(Guid.NewGuid().ToString("N"), parentUid, nm, false, now, now);
                _nodes.Add(node);
                WriteLocked();
                return node.Uid;
            }
        }

        public static string TryAddFavorite(string parentUid, string name, string kind, string target, string openWith, bool confirmOpen, out string error)
        {
            error = "";
            if (LauncherSystemCatalog.IsSystemUid(parentUid))
            {
                error = "不能在系统目录中新增。";
                return null;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "请填写名称。";
                return null;
            }
            if (!LauncherTarget.TryValidate(kind, target, out error))
                return null;
            if (!LauncherTarget.TryValidateOpenWith(kind, openWith, out error))
                return null;
            EnsureLoaded();
            lock (_sync)
            {
                if (!PrepareParentLocked(ref parentUid, false, out error))
                    return null;
                var now = DateTime.Now;
                var node = new LauncherNode
                {
                    Uid = Guid.NewGuid().ToString("N"),
                    Type = LauncherTree.TypeFavorite,
                    ParentUid = parentUid,
                    Name = name.Trim(),
                    Kind = kind,
                    Target = target.Trim(),
                    OpenWith = kind == LauncherTarget.KindFile && !string.IsNullOrWhiteSpace(openWith) ? openWith.Trim() : "",
                    ConfirmOpen = confirmOpen,
                    Pinned = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _nodes.Add(node);
                WriteLocked();
                return node.Uid;
            }
        }

        public static bool TryUpdateCategory(string uid, string name, bool pinned, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(uid))
            {
                error = "目录不存在。";
                return false;
            }
            if (LauncherSystemCatalog.IsSystemUid(uid))
            {
                error = "系统快捷方式不能修改。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "请填写目录名称。";
                return false;
            }
            EnsureLoaded();
            lock (_sync)
            {
                var cur = LauncherTree.Find(_nodes, uid);
                if (cur == null || !cur.IsCategory)
                {
                    error = "目录不存在。";
                    return false;
                }
                cur.Name = name.Trim();
                cur.Pinned = pinned;
                cur.UpdatedAt = DateTime.Now;
                if (cur.CreatedAt == default(DateTime))
                    cur.CreatedAt = cur.UpdatedAt;
                WriteLocked();
                return true;
            }
        }

        public static bool TryUpdateFavorite(string uid, string name, string kind, string target, string openWith, bool pinned, bool confirmOpen, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(uid))
            {
                error = "收藏不存在。";
                return false;
            }
            if (LauncherSystemCatalog.IsSystemUid(uid))
            {
                error = "系统快捷方式不能修改。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "请填写名称。";
                return false;
            }
            if (!LauncherTarget.TryValidate(kind, target, out error))
                return false;
            if (!LauncherTarget.TryValidateOpenWith(kind, openWith, out error))
                return false;
            EnsureLoaded();
            lock (_sync)
            {
                var cur = LauncherTree.Find(_nodes, uid);
                if (cur == null || cur.IsCategory)
                {
                    error = "收藏不存在。";
                    return false;
                }
                cur.Name = name.Trim();
                cur.Kind = kind;
                cur.Target = target.Trim();
                cur.OpenWith = kind == LauncherTarget.KindFile && !string.IsNullOrWhiteSpace(openWith) ? openWith.Trim() : "";
                cur.Pinned = pinned;
                cur.ConfirmOpen = confirmOpen;
                cur.UpdatedAt = DateTime.Now;
                if (cur.CreatedAt == default(DateTime))
                    cur.CreatedAt = cur.UpdatedAt;
                WriteLocked();
                return true;
            }
        }

        public static bool TryMove(string uid, string newParentUid, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(uid))
            {
                error = "收藏不存在。";
                return false;
            }
            if (LauncherSystemCatalog.IsSystemUid(uid))
            {
                error = "系统快捷方式不能移动。";
                return false;
            }
            EnsureLoaded();
            lock (_sync)
            {
                if (!LauncherTree.CanMoveFavorite(_nodes, uid, newParentUid, out error))
                    return false;
                var cur = LauncherTree.Find(_nodes, uid);
                if (cur == null)
                {
                    error = "收藏不存在。";
                    return false;
                }
                string p = newParentUid ?? "";
                if ((cur.ParentUid ?? "") == p)
                    return true;
                cur.ParentUid = p;
                cur.Name = LauncherTree.UniqueSiblingName(_nodes, p, cur.Name, cur.Uid);
                cur.UpdatedAt = DateTime.Now;
                WriteLocked();
                return true;
            }
        }

        public static bool Remove(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            if (LauncherSystemCatalog.IsSystemUid(uid)) return false;
            EnsureLoaded();
            lock (_sync)
            {
                if (LauncherTree.Find(_nodes, uid) == null) return false;
                var ids = LauncherTree.SubtreeUids(_nodes, uid);
                var drop = new HashSet<string>(ids);
                for (int i = _nodes.Count - 1; i >= 0; i--)
                {
                    if (drop.Contains(_nodes[i].Uid))
                        _nodes.RemoveAt(i);
                }
                for (int i = 0; i < ids.Count; i++)
                    RemoveRecentLocked(LauncherHit.FavoriteId(ids[i]));
                WriteLocked();
                return true;
            }
        }

        public static bool SetPinned(string uid, bool pinned)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            if (LauncherSystemCatalog.IsSystemFolder(uid)) return false;
            EnsureLoaded();
            lock (_sync)
            {
                if (LauncherSystemCatalog.IsSystemItem(uid))
                {
                    var built = LauncherSystemCatalog.BuildCurrent(_systemPinned);
                    if (LauncherTree.Find(built, uid) == null) return false;
                    if (pinned)
                    {
                        if (!_systemPinned.Contains(uid)) _systemPinned.Add(uid);
                    }
                    else
                    {
                        _systemPinned.Remove(uid);
                    }
                    WriteLocked();
                    return true;
                }
                var cur = LauncherTree.Find(_nodes, uid);
                if (cur == null) return false;
                cur.Pinned = pinned;
                cur.UpdatedAt = DateTime.Now;
                WriteLocked();
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
            if (LauncherSystemCatalog.IsSystemUid(uid))
            {
                error = "系统快捷方式不能复制。";
                return null;
            }
            EnsureLoaded();
            lock (_sync)
            {
                var src = LauncherTree.Find(_nodes, uid);
                if (src == null)
                {
                    error = "条目不存在。";
                    return null;
                }
                var copies = LauncherTree.DuplicateSubtree(_nodes, uid);
                if (copies.Count == 0)
                {
                    error = "无法复制。";
                    return null;
                }
                if (_nodes.Count + copies.Count > LauncherTree.MaxNodes)
                {
                    error = "节点已满（最多 " + LauncherTree.MaxNodes + " 条）。";
                    return null;
                }
                if (LauncherTree.ChildDepth(_nodes, src.ParentUid) >= LauncherTree.MaxDepth)
                {
                    error = "目录层级已达上限。";
                    return null;
                }
                for (int i = 0; i < copies.Count; i++)
                    _nodes.Add(copies[i]);
                WriteLocked();
                return copies[0].Uid;
            }
        }

        public static int CountDescendants(string uid)
        {
            EnsureLoaded();
            lock (_sync)
            {
                return LauncherTree.CountDescendants(_nodes, uid);
            }
        }

        public static void TouchRecent(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            EnsureLoaded();
            lock (_sync)
            {
                RemoveRecentLocked(id);
                _recents.Insert(0, id);
                while (_recents.Count > MaxRecents)
                    _recents.RemoveAt(_recents.Count - 1);
                WriteLocked();
            }
        }

        private static bool PrepareParentLocked(ref string parentUid, bool forCategory, out string error)
        {
            error = "";
            parentUid = parentUid ?? "";
            if (LauncherSystemCatalog.IsSystemUid(parentUid))
            {
                error = "不能在系统目录中新增。";
                return false;
            }
            if (parentUid.Length > 0)
            {
                var parent = LauncherTree.Find(_nodes, parentUid);
                if (parent == null || !parent.IsCategory)
                {
                    error = "父目录不存在。";
                    return false;
                }
            }
            if (_nodes.Count >= LauncherTree.MaxNodes)
            {
                error = "节点已满（最多 " + LauncherTree.MaxNodes + " 条）。";
                return false;
            }
            if (!LauncherTree.CanAddChild(_nodes, parentUid))
            {
                error = forCategory ? "目录层级已达上限。" : "无法在此层级添加收藏。";
                return false;
            }
            return true;
        }

        private static void EnsureLoaded()
        {
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
            }
        }

        private static void LoadLocked()
        {
            _nodes = new List<LauncherNode>();
            _recents = new List<string>();
            _systemPinned = new List<string>();
            _writeBlocked = false;
            _loaded = true;
            string path = DataPaths.LauncherJson;
            if (!File.Exists(path)) return;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var loaded = LauncherFile.TryLoad(json, MaxRecents);
                if (!loaded.Ok)
                {
                    RecoverCorruptLocked(path);
                    return;
                }
                _nodes = loaded.Nodes ?? new List<LauncherNode>();
                _recents = loaded.Recents ?? new List<string>();
                _systemPinned = loaded.SystemPinned ?? new List<string>();
                if (loaded.NeedsRewrite)
                {
                    try { WriteLocked(); }
                    catch { }
                }
            }
            catch
            {
                RecoverCorruptLocked(path);
            }
        }

        private static void RecoverCorruptLocked(string path)
        {
            if (!DataFileGuard.TryQuarantine(path))
            {
                _writeBlocked = true;
                return;
            }
            try { WriteLocked(); }
            catch { }
        }

        private static void WriteLocked()
        {
            if (_writeBlocked) return;
            string path = DataPaths.LauncherJson;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = LauncherFile.Save(CloneList(_nodes), new List<string>(_recents), new List<string>(_systemPinned));
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static List<LauncherNode> MergeLocked()
        {
            var merged = LauncherSystemCatalog.BuildCurrent(_systemPinned);
            for (int i = 0; i < _nodes.Count; i++)
                merged.Add(LauncherTree.Clone(_nodes[i]));
            return merged;
        }

        private static void RemoveRecentLocked(string id)
        {
            for (int i = _recents.Count - 1; i >= 0; i--)
            {
                if (_recents[i] == id) _recents.RemoveAt(i);
            }
        }

        private static List<LauncherNode> CloneList(List<LauncherNode> src)
        {
            var list = new List<LauncherNode>(src.Count);
            for (int i = 0; i < src.Count; i++)
                list.Add(LauncherTree.Clone(src[i]));
            return list;
        }
    }
}
