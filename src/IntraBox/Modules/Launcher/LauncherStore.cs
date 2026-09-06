using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IntraBox.Core;
using Newtonsoft.Json;

namespace IntraBox.Modules.Launcher
{
    public sealed class LauncherState
    {
        public List<LauncherNode> Nodes { get; set; }
        public List<LauncherFavorite> Favorites { get; set; }
        public List<string> Recents { get; set; }
    }

    /// <summary>读写数据根 launcher.json。节点最多 200，收藏深度 8，最近最多 30。</summary>
    public static class LauncherStore
    {
        public const int MaxRecents = 30;

        private static readonly object _sync = new object();
        private static List<LauncherNode> _nodes = new List<LauncherNode>();
        private static List<string> _recents = new List<string>();
        private static bool _loaded;

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
                if (!_loaded) return;
                try { WriteLocked(); }
                catch { }
            }
        }

        public static List<LauncherNode> SnapshotNodes()
        {
            EnsureLoaded();
            lock (_sync)
            {
                return CloneList(_nodes);
            }
        }

        public static List<LauncherNode> SnapshotFavorites()
        {
            EnsureLoaded();
            lock (_sync)
            {
                var list = new List<LauncherNode>();
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i] != null && !_nodes[i].IsCategory)
                        list.Add(LauncherTree.Clone(_nodes[i]));
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
                return LauncherTree.Clone(LauncherTree.Find(_nodes, uid));
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
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "请填写分类名称。";
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
                error = "分类不存在。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "请填写分类名称。";
                return false;
            }
            EnsureLoaded();
            lock (_sync)
            {
                var cur = LauncherTree.Find(_nodes, uid);
                if (cur == null || !cur.IsCategory)
                {
                    error = "分类不存在。";
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
            EnsureLoaded();
            lock (_sync)
            {
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
                    error = "分类层级已达上限。";
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
            if (parentUid.Length > 0)
            {
                var parent = LauncherTree.Find(_nodes, parentUid);
                if (parent == null || !parent.IsCategory)
                {
                    error = "父分类不存在。";
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
                error = forCategory ? "分类层级已达上限。" : "无法在此层级添加收藏。";
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
            _loaded = true;
            string path = DataPaths.LauncherJson;
            if (!File.Exists(path)) return;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return;
                var state = JsonConvert.DeserializeObject<LauncherState>(json);
                if (state == null) return;
                if (state.Nodes != null && state.Nodes.Count > 0)
                    _nodes = LauncherTree.Sanitize(state.Nodes);
                else if (state.Favorites != null && state.Favorites.Count > 0)
                {
                    _nodes = LauncherTree.MigrateFromFavorites(state.Favorites);
                    try { WriteLocked(); }
                    catch { }
                }
                if (state.Recents != null)
                {
                    for (int i = 0; i < state.Recents.Count && _recents.Count < MaxRecents; i++)
                    {
                        string id = state.Recents[i];
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        if (_recents.Contains(id)) continue;
                        _recents.Add(id);
                    }
                }
            }
            catch
            {
            }
        }

        private static void WriteLocked()
        {
            string path = DataPaths.LauncherJson;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var state = new LauncherState
            {
                Nodes = CloneList(_nodes),
                Recents = new List<string>(_recents)
            };
            string json = JsonConvert.SerializeObject(state, Formatting.Indented);
            File.WriteAllText(path, json, new UTF8Encoding(false));
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
