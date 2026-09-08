using System;
using System.Collections.Generic;

namespace IntraBox.Modules.Notes
{
    /// <summary>笔记目录树：展平、深度、移动校验。不碰磁盘。</summary>
    public static class NoteTree
    {
        public const int MaxNodes = 200;
        public const int MaxDepth = 8;

        public static List<NoteItem> Sanitize(IList<NoteItem> raw)
        {
            var staged = new List<NoteItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (raw == null) return staged;
            for (int i = 0; i < raw.Count; i++)
            {
                var n = Normalize(raw[i]);
                if (n == null) continue;
                if (!seen.Add(n.Uid)) continue;
                staged.Add(n);
            }
            var byId = IndexByUid(staged);
            for (int i = 0; i < staged.Count; i++)
            {
                var n = staged[i];
                string p = n.ParentUid ?? "";
                if (p.Length == 0) continue;
                NoteItem parent;
                if (!byId.TryGetValue(p, out parent) || !parent.IsFolder || p == n.Uid)
                    n.ParentUid = "";
            }
            BreakCycles(staged, byId);
            var accepted = new List<NoteItem>();
            var acceptedIds = new HashSet<string>(StringComparer.Ordinal);
            var children = ChildrenMap(staged);
            var roots = ChildrenOf(children, "");
            SortSiblings(roots, "title", true);
            for (int i = 0; i < roots.Count; i++)
                CollectBounded(roots[i], 0, children, accepted, acceptedIds);
            return accepted;
        }

        public static NoteItem Normalize(NoteItem src)
        {
            if (src == null || string.IsNullOrEmpty(src.Uid)) return null;
            var n = Clone(src);
            if (n.Type != NoteNodeType.Folder && n.Type != NoteNodeType.Note)
                n.Type = NoteNodeType.Note;
            n.ParentUid = n.ParentUid ?? "";
            if (n.IsFolder)
            {
                n.Kind = NoteKind.Rich;
                if (string.IsNullOrWhiteSpace(n.Title)) n.Title = NoteKind.DefaultFolderTitle;
                else n.Title = n.Title.Trim();
            }
            else
            {
                if (n.Kind != NoteKind.Markdown) n.Kind = NoteKind.Rich;
                if (string.IsNullOrWhiteSpace(n.Title)) n.Title = NoteKind.DefaultTitle(n.Kind);
                else n.Title = n.Title.Trim();
            }
            return n;
        }

        public static List<NoteFlatRow> Flatten(IList<NoteItem> items, ICollection<string> collapsed,
            string query, int kindFilter, string sortKey, bool sortAsc)
        {
            var result = new List<NoteFlatRow>();
            var list = Sanitize(items);
            var children = ChildrenMap(list);
            HashSet<string> include = BuildInclude(list, children, query, kindFilter);
            Walk("", 0, children, collapsed, include, result, sortKey, sortAsc);
            return result;
        }

        /// <summary>仅目录、全部展开，供移动目标选择。</summary>
        public static List<NoteFlatRow> FlattenFolders(IList<NoteItem> items)
        {
            var result = new List<NoteFlatRow>();
            var list = Sanitize(items);
            var children = ChildrenMap(list);
            WalkFolders("", 0, children, result);
            return result;
        }

        public static bool CanMoveNote(IList<NoteItem> items, string uid, string newParentUid, out string error)
        {
            error = "";
            var n = Find(items, uid);
            if (n == null || n.IsFolder)
            {
                error = "只能移动笔记。";
                return false;
            }
            string p = newParentUid ?? "";
            if (p.Length > 0)
            {
                var parent = Find(items, p);
                if (parent == null || !parent.IsFolder)
                {
                    error = "目标目录不存在。";
                    return false;
                }
            }
            if ((n.ParentUid ?? "") == p)
                return true;
            if (ChildDepth(items, p) >= MaxDepth)
            {
                error = "目录层级已达上限。";
                return false;
            }
            return true;
        }

        public static int ChildDepth(IList<NoteItem> items, string parentUid)
        {
            var byId = IndexByUid(items);
            int d = 0;
            string p = parentUid ?? "";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(p))
            {
                if (!seen.Add(p)) break;
                d++;
                NoteItem n;
                if (!byId.TryGetValue(p, out n)) break;
                p = n.ParentUid ?? "";
            }
            return d;
        }

        public static bool CanAddChild(IList<NoteItem> items, string parentUid)
        {
            if (items != null && items.Count >= MaxNodes) return false;
            return ChildDepth(items, parentUid) < MaxDepth;
        }

        public static int CountDescendants(IList<NoteItem> items, string uid)
        {
            if (string.IsNullOrEmpty(uid)) return 0;
            var children = ChildrenMap(items);
            return CountDescLocked(uid, children);
        }

        public static List<string> SubtreeUids(IList<NoteItem> items, string uid)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(uid)) return ids;
            ids.Add(uid);
            var children = ChildrenMap(items);
            CollectUids(uid, children, ids);
            return ids;
        }

        public static string UniqueSiblingName(IList<NoteItem> items, string parentUid, string baseName, string exceptUid)
        {
            string name = string.IsNullOrWhiteSpace(baseName) ? NoteKind.DefaultFolderTitle : baseName.Trim();
            if (!NameTaken(items, parentUid, name, exceptUid)) return name;
            for (int n = 2; n < 1000; n++)
            {
                string cand = name + " " + n;
                if (!NameTaken(items, parentUid, cand, exceptUid)) return cand;
            }
            return name + " " + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public static List<NoteItem> DuplicateSubtree(IList<NoteItem> items, string uid, List<string> sourceUids)
        {
            var result = new List<NoteItem>();
            if (sourceUids != null) sourceUids.Clear();
            if (items == null || string.IsNullOrEmpty(uid)) return result;
            NoteItem src = Find(items, uid);
            if (src == null) return result;
            string copyName = UniqueSiblingName(items, src.ParentUid, (src.Title ?? "") + " 副本", null);
            var children = ChildrenMap(items);
            DuplicateWalk(src, src.ParentUid, copyName, children, result, sourceUids);
            return result;
        }

        public static List<NoteItem> DuplicateSubtree(IList<NoteItem> items, string uid)
        {
            return DuplicateSubtree(items, uid, null);
        }

        public static NoteItem Find(IList<NoteItem> items, string uid)
        {
            if (items == null || string.IsNullOrEmpty(uid)) return null;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].Uid == uid)
                    return items[i];
            }
            return null;
        }

        public static NoteItem Clone(NoteItem s)
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

        private static Dictionary<string, NoteItem> IndexByUid(IList<NoteItem> items)
        {
            var map = new Dictionary<string, NoteItem>(StringComparer.Ordinal);
            if (items == null) return map;
            for (int i = 0; i < items.Count; i++)
            {
                var n = items[i];
                if (n == null || string.IsNullOrEmpty(n.Uid) || map.ContainsKey(n.Uid)) continue;
                map[n.Uid] = n;
            }
            return map;
        }

        private static void BreakCycles(List<NoteItem> items, Dictionary<string, NoteItem> byId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var n = items[i];
                var seen = new HashSet<string>(StringComparer.Ordinal);
                seen.Add(n.Uid);
                string p = n.ParentUid ?? "";
                while (!string.IsNullOrEmpty(p))
                {
                    if (!seen.Add(p))
                    {
                        n.ParentUid = "";
                        break;
                    }
                    NoteItem parent;
                    if (!byId.TryGetValue(p, out parent))
                    {
                        n.ParentUid = "";
                        break;
                    }
                    p = parent.ParentUid ?? "";
                }
            }
        }

        private static Dictionary<string, List<NoteItem>> ChildrenMap(IList<NoteItem> items)
        {
            var map = new Dictionary<string, List<NoteItem>>(StringComparer.Ordinal);
            if (items == null) return map;
            for (int i = 0; i < items.Count; i++)
            {
                var n = items[i];
                if (n == null) continue;
                string p = n.ParentUid ?? "";
                List<NoteItem> list;
                if (!map.TryGetValue(p, out list))
                {
                    list = new List<NoteItem>();
                    map[p] = list;
                }
                list.Add(n);
            }
            return map;
        }

        private static List<NoteItem> ChildrenOf(Dictionary<string, List<NoteItem>> children, string parentUid)
        {
            List<NoteItem> list;
            if (!children.TryGetValue(parentUid ?? "", out list) || list == null)
                return new List<NoteItem>();
            return new List<NoteItem>(list);
        }

        private static void SortSiblings(List<NoteItem> list, string sortKey, bool sortAsc)
        {
            list.Sort((a, b) => CompareSiblings(a, b, sortKey, sortAsc));
        }

        private static int CompareSiblings(NoteItem a, NoteItem b, string sortKey, bool sortAsc)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int pin = b.Pinned.CompareTo(a.Pinned);
            if (pin != 0) return pin;
            int folder = (b.IsFolder ? 1 : 0).CompareTo(a.IsFolder ? 1 : 0);
            if (folder != 0) return folder;
            int c;
            if (sortKey == "kind")
                c = a.Kind.CompareTo(b.Kind);
            else if (sortKey == "title")
                c = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            else if (sortKey == "created")
                c = a.CreatedAt.CompareTo(b.CreatedAt);
            else if (sortKey == "pin")
                c = a.Pinned.CompareTo(b.Pinned);
            else
                c = a.UpdatedAt.CompareTo(b.UpdatedAt);
            if (!sortAsc) c = -c;
            if (c == 0) c = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            if (c == 0) c = string.Compare(a.Uid, b.Uid, StringComparison.Ordinal);
            return c;
        }

        private static void CollectBounded(NoteItem n, int depth, Dictionary<string, List<NoteItem>> children,
            List<NoteItem> accepted, HashSet<string> acceptedIds)
        {
            if (n == null || accepted.Count >= MaxNodes) return;
            if (depth >= MaxDepth) return;
            if (!acceptedIds.Add(n.Uid)) return;
            accepted.Add(n);
            if (!n.IsFolder) return;
            var kids = ChildrenOf(children, n.Uid);
            SortSiblings(kids, "title", true);
            for (int i = 0; i < kids.Count; i++)
                CollectBounded(kids[i], depth + 1, children, accepted, acceptedIds);
        }

        private static HashSet<string> BuildInclude(List<NoteItem> items, Dictionary<string, List<NoteItem>> children,
            string query, int kindFilter)
        {
            bool hasQuery = !string.IsNullOrWhiteSpace(query);
            bool hasKind = kindFilter == 1 || kindFilter == 2;
            if (!hasQuery && !hasKind) return null;
            string q = hasQuery ? query.Trim() : "";
            var include = new HashSet<string>(StringComparer.Ordinal);
            var byId = IndexByUid(items);
            for (int i = 0; i < items.Count; i++)
            {
                var n = items[i];
                if (n.IsFolder)
                {
                    if (hasQuery && TitleMatches(n.Title, q))
                    {
                        include.Add(n.Uid);
                        var ids = new List<string>();
                        CollectUids(n.Uid, children, ids);
                        for (int j = 0; j < ids.Count; j++)
                        {
                            var child = Find(items, ids[j]);
                            if (child == null) continue;
                            if (child.IsFolder || KindOk(child, kindFilter))
                                include.Add(child.Uid);
                        }
                    }
                    continue;
                }
                if (!KindOk(n, kindFilter)) continue;
                if (hasQuery && !TitleMatches(n.Title, q)) continue;
                include.Add(n.Uid);
            }
            var extra = new List<string>(include);
            for (int i = 0; i < extra.Count; i++)
                AddAncestors(extra[i], byId, include);
            return include;
        }

        private static bool KindOk(NoteItem n, int kindFilter)
        {
            if (n == null || n.IsFolder) return false;
            if (kindFilter == 1) return n.Kind == NoteKind.Rich;
            if (kindFilter == 2) return n.Kind == NoteKind.Markdown;
            return true;
        }

        private static bool TitleMatches(string title, string query)
        {
            if (string.IsNullOrEmpty(title)) return false;
            return title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddAncestors(string uid, Dictionary<string, NoteItem> byId, HashSet<string> include)
        {
            NoteItem n;
            if (!byId.TryGetValue(uid, out n)) return;
            string p = n.ParentUid ?? "";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(p) && seen.Add(p))
            {
                include.Add(p);
                if (!byId.TryGetValue(p, out n)) break;
                p = n.ParentUid ?? "";
            }
        }

        private static void Walk(string parentUid, int depth, Dictionary<string, List<NoteItem>> children,
            ICollection<string> collapsed, HashSet<string> include, List<NoteFlatRow> result,
            string sortKey, bool sortAsc)
        {
            var kids = ChildrenOf(children, parentUid);
            SortSiblings(kids, sortKey, sortAsc);
            for (int i = 0; i < kids.Count; i++)
            {
                var n = kids[i];
                if (include != null && !include.Contains(n.Uid)) continue;
                var visibleKids = VisibleChildren(children, n.Uid, include);
                bool hasChildren = n.IsFolder && visibleKids.Count > 0;
                bool expanded = hasChildren && (include != null || collapsed == null || !collapsed.Contains(n.Uid));
                result.Add(new NoteFlatRow
                {
                    Item = n,
                    Depth = depth,
                    HasChildren = hasChildren,
                    Expanded = expanded
                });
                if (n.IsFolder && expanded)
                    Walk(n.Uid, depth + 1, children, collapsed, include, result, sortKey, sortAsc);
            }
        }

        private static void WalkFolders(string parentUid, int depth, Dictionary<string, List<NoteItem>> children, List<NoteFlatRow> result)
        {
            var kids = ChildrenOf(children, parentUid);
            SortSiblings(kids, "title", true);
            for (int i = 0; i < kids.Count; i++)
            {
                var n = kids[i];
                if (!n.IsFolder) continue;
                var catKids = ChildrenOf(children, n.Uid);
                bool has = false;
                for (int j = 0; j < catKids.Count; j++)
                {
                    if (catKids[j].IsFolder) { has = true; break; }
                }
                result.Add(new NoteFlatRow
                {
                    Item = n,
                    Depth = depth,
                    HasChildren = has,
                    Expanded = true
                });
                WalkFolders(n.Uid, depth + 1, children, result);
            }
        }

        private static List<NoteItem> VisibleChildren(Dictionary<string, List<NoteItem>> children, string parentUid, HashSet<string> include)
        {
            var kids = ChildrenOf(children, parentUid);
            if (include == null) return kids;
            var vis = new List<NoteItem>();
            for (int i = 0; i < kids.Count; i++)
            {
                if (include.Contains(kids[i].Uid))
                    vis.Add(kids[i]);
            }
            return vis;
        }

        private static int CountDescLocked(string uid, Dictionary<string, List<NoteItem>> children)
        {
            var kids = ChildrenOf(children, uid);
            int n = kids.Count;
            for (int i = 0; i < kids.Count; i++)
            {
                if (kids[i].IsFolder)
                    n += CountDescLocked(kids[i].Uid, children);
            }
            return n;
        }

        private static void CollectUids(string uid, Dictionary<string, List<NoteItem>> children, ICollection<string> ids)
        {
            var kids = ChildrenOf(children, uid);
            for (int i = 0; i < kids.Count; i++)
            {
                ids.Add(kids[i].Uid);
                if (kids[i].IsFolder)
                    CollectUids(kids[i].Uid, children, ids);
            }
        }

        private static bool NameTaken(IList<NoteItem> items, string parentUid, string name, string exceptUid)
        {
            string parent = parentUid ?? "";
            if (items == null) return false;
            for (int i = 0; i < items.Count; i++)
            {
                var n = items[i];
                if (n == null) continue;
                if ((n.ParentUid ?? "") != parent) continue;
                if (!string.IsNullOrEmpty(exceptUid) && n.Uid == exceptUid) continue;
                if (string.Equals(n.Title, name, StringComparison.CurrentCultureIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void DuplicateWalk(NoteItem src, string parentUid, string name,
            Dictionary<string, List<NoteItem>> children, List<NoteItem> result, List<string> sourceUids)
        {
            var now = DateTime.Now;
            var copy = Clone(src);
            copy.Uid = Guid.NewGuid().ToString("N");
            copy.ParentUid = parentUid ?? "";
            copy.Title = name;
            copy.Pinned = false;
            copy.CreatedAt = now;
            copy.UpdatedAt = now;
            result.Add(copy);
            if (sourceUids != null) sourceUids.Add(src.Uid);
            if (!src.IsFolder) return;
            var kids = ChildrenOf(children, src.Uid);
            SortSiblings(kids, "title", true);
            for (int i = 0; i < kids.Count; i++)
                DuplicateWalk(kids[i], copy.Uid, kids[i].Title, children, result, sourceUids);
        }
    }
}
