using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>启动器树：迁移、展平、复制命名、深度。不碰磁盘。</summary>
    public static class LauncherTree
    {
        public const string TypeCategory = "category";
        public const string TypeFavorite = "favorite";
        public const int MaxNodes = 200;
        public const int MaxDepth = 8;

        public static List<LauncherNode> MigrateFromFavorites(IList<LauncherFavorite> old)
        {
            var nodes = new List<LauncherNode>();
            if (old == null || old.Count == 0) return nodes;
            var catUid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < old.Count; i++)
            {
                var f = old[i];
                if (f == null || string.IsNullOrEmpty(f.Uid)) continue;
                string err;
                if (!LauncherTarget.TryValidate(f.Kind, f.Target, out err)) continue;
                if (string.IsNullOrWhiteSpace(f.Name)) continue;
                string cat = LauncherTarget.NormalizeCategory(f.Category);
                string parent = "";
                if (cat.Length > 0)
                {
                    string uid;
                    if (!catUid.TryGetValue(cat, out uid))
                    {
                        uid = Guid.NewGuid().ToString("N");
                        var now = f.CreatedAt == default(DateTime) ? DateTime.Now : f.CreatedAt;
                        nodes.Add(NewCategory(uid, "", cat, false, now, now));
                        catUid[cat] = uid;
                    }
                    parent = catUid[cat];
                }
                nodes.Add(CloneFavoriteFromLegacy(f, parent));
            }
            return Sanitize(nodes);
        }

        public static List<LauncherNode> Sanitize(IList<LauncherNode> raw)
        {
            var staged = new List<LauncherNode>();
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
                LauncherNode parent;
                if (!byId.TryGetValue(p, out parent) || !parent.IsCategory || p == n.Uid)
                    n.ParentUid = "";
            }
            BreakCycles(staged, byId);
            var accepted = new List<LauncherNode>();
            var acceptedIds = new HashSet<string>(StringComparer.Ordinal);
            var children = ChildrenMap(staged);
            var roots = ChildrenOf(children, "");
            SortSiblings(roots);
            for (int i = 0; i < roots.Count; i++)
                CollectBounded(roots[i], 0, children, accepted, acceptedIds);
            return accepted;
        }

        public static List<LauncherFlatRow> Flatten(IList<LauncherNode> nodes, ICollection<string> collapsed, string query)
        {
            var result = new List<LauncherFlatRow>();
            var list = Sanitize(nodes);
            var children = ChildrenMap(list);
            HashSet<string> include = null;
            if (!string.IsNullOrWhiteSpace(query))
            {
                include = BuildInclude(list, children, query.Trim());
                if (include.Count == 0) return result;
            }
            Walk("", 0, children, collapsed, include, result);
            return result;
        }

        /// <summary>仅目录、全部展开，供移动目标选择。</summary>
        public static List<LauncherFlatRow> FlattenCategories(IList<LauncherNode> nodes)
        {
            var result = new List<LauncherFlatRow>();
            var list = Sanitize(nodes);
            var children = ChildrenMap(list);
            WalkCategories("", 0, children, result);
            return result;
        }

        public static bool CanMoveFavorite(IList<LauncherNode> nodes, string uid, string newParentUid, out string error)
        {
            error = "";
            var n = Find(nodes, uid);
            if (n == null || n.IsCategory)
            {
                error = "只能移动收藏。";
                return false;
            }
            string p = newParentUid ?? "";
            if (LauncherSystemCatalog.IsSystemUid(uid))
            {
                error = "系统快捷方式不能移动。";
                return false;
            }
            if (p.Length > 0)
            {
                var parent = Find(nodes, p);
                if (parent == null || !parent.IsCategory)
                {
                    error = "目标目录不存在。";
                    return false;
                }
                if (LauncherSystemCatalog.IsSystemUid(p))
                {
                    error = "不能移入系统目录。";
                    return false;
                }
            }
            if ((n.ParentUid ?? "") == p)
                return true;
            if (ChildDepth(nodes, p) >= MaxDepth)
            {
                error = "目录层级已达上限。";
                return false;
            }
            return true;
        }

        public static int ChildDepth(IList<LauncherNode> nodes, string parentUid)
        {
            var byId = IndexByUid(nodes);
            int d = 0;
            string p = parentUid ?? "";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(p))
            {
                if (!seen.Add(p)) break;
                d++;
                LauncherNode n;
                if (!byId.TryGetValue(p, out n)) break;
                p = n.ParentUid ?? "";
            }
            return d;
        }

        public static bool CanAddChild(IList<LauncherNode> nodes, string parentUid)
        {
            if (nodes != null && nodes.Count >= MaxNodes) return false;
            return ChildDepth(nodes, parentUid) < MaxDepth;
        }

        public static int CountDescendants(IList<LauncherNode> nodes, string uid)
        {
            if (string.IsNullOrEmpty(uid)) return 0;
            var children = ChildrenMap(nodes);
            return CountDescLocked(uid, children);
        }

        public static List<string> SubtreeUids(IList<LauncherNode> nodes, string uid)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(uid)) return ids;
            ids.Add(uid);
            var children = ChildrenMap(nodes);
            CollectUids(uid, children, ids);
            return ids;
        }

        public static string ParentPath(IList<LauncherNode> nodes, string parentUid)
        {
            var byId = IndexByUid(nodes);
            var names = new List<string>();
            string p = parentUid ?? "";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(p))
            {
                if (!seen.Add(p)) break;
                LauncherNode n;
                if (!byId.TryGetValue(p, out n)) break;
                names.Add(n.Name ?? "");
                p = n.ParentUid ?? "";
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        public static string UniqueSiblingName(IList<LauncherNode> nodes, string parentUid, string baseName, string exceptUid)
        {
            string name = string.IsNullOrWhiteSpace(baseName) ? "未命名" : baseName.Trim();
            if (!NameTaken(nodes, parentUid, name, exceptUid)) return name;
            for (int n = 2; n < 1000; n++)
            {
                string cand = name + " " + n;
                if (!NameTaken(nodes, parentUid, cand, exceptUid)) return cand;
            }
            return name + " " + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public static List<LauncherNode> DuplicateSubtree(IList<LauncherNode> nodes, string uid)
        {
            var result = new List<LauncherNode>();
            if (nodes == null || string.IsNullOrEmpty(uid)) return result;
            LauncherNode src = Find(nodes, uid);
            if (src == null) return result;
            string copyName = UniqueSiblingName(nodes, src.ParentUid, (src.Name ?? "") + " 副本", null);
            var children = ChildrenMap(nodes);
            DuplicateWalk(src, src.ParentUid, copyName, children, result);
            return result;
        }

        public static LauncherNode Find(IList<LauncherNode> nodes, string uid)
        {
            if (nodes == null || string.IsNullOrEmpty(uid)) return null;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null && nodes[i].Uid == uid)
                    return nodes[i];
            }
            return null;
        }

        public static LauncherNode Clone(LauncherNode src)
        {
            if (src == null) return null;
            return new LauncherNode
            {
                Uid = src.Uid,
                Type = src.Type,
                ParentUid = src.ParentUid ?? "",
                Name = src.Name ?? "",
                Kind = src.Kind ?? "",
                Target = src.Target ?? "",
                OpenWith = src.OpenWith ?? "",
                ConfirmOpen = src.ConfirmOpen,
                Pinned = src.Pinned,
                CreatedAt = src.CreatedAt,
                UpdatedAt = src.UpdatedAt
            };
        }

        public static LauncherNode NewCategory(string uid, string parentUid, string name, bool pinned, DateTime created, DateTime updated)
        {
            return new LauncherNode
            {
                Uid = uid,
                Type = TypeCategory,
                ParentUid = parentUid ?? "",
                Name = name ?? "",
                Kind = "",
                Target = "",
                OpenWith = "",
                ConfirmOpen = null,
                Pinned = pinned,
                CreatedAt = created,
                UpdatedAt = updated
            };
        }

        private static LauncherNode CloneFavoriteFromLegacy(LauncherFavorite f, string parentUid)
        {
            return new LauncherNode
            {
                Uid = f.Uid,
                Type = TypeFavorite,
                ParentUid = parentUid ?? "",
                Name = f.Name.Trim(),
                Kind = f.Kind,
                Target = f.Target.Trim(),
                OpenWith = "",
                ConfirmOpen = true,
                Pinned = f.Pinned,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            };
        }

        private static LauncherNode Normalize(LauncherNode src)
        {
            if (src == null || string.IsNullOrEmpty(src.Uid)) return null;
            string type = src.Type;
            if (type != TypeCategory && type != TypeFavorite)
            {
                if (!string.IsNullOrWhiteSpace(src.Kind) && !string.IsNullOrWhiteSpace(src.Target))
                    type = TypeFavorite;
                else
                    type = TypeCategory;
            }
            if (string.IsNullOrWhiteSpace(src.Name)) return null;
            var n = Clone(src);
            n.Type = type;
            n.Name = src.Name.Trim();
            n.ParentUid = src.ParentUid ?? "";
            if (n.IsCategory)
            {
                n.Kind = "";
                n.Target = "";
                n.OpenWith = "";
                n.ConfirmOpen = null;
                return n;
            }
            string err;
            if (!LauncherTarget.TryValidate(n.Kind, n.Target, out err)) return null;
            n.Target = n.Target.Trim();
            if (!LauncherTarget.TryValidateOpenWith(n.Kind, n.OpenWith, out err))
                n.OpenWith = "";
            else
                n.OpenWith = string.IsNullOrWhiteSpace(n.OpenWith) ? "" : n.OpenWith.Trim();
            return n;
        }

        private static Dictionary<string, LauncherNode> IndexByUid(IList<LauncherNode> nodes)
        {
            var map = new Dictionary<string, LauncherNode>(StringComparer.Ordinal);
            if (nodes == null) return map;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n == null || string.IsNullOrEmpty(n.Uid) || map.ContainsKey(n.Uid)) continue;
                map[n.Uid] = n;
            }
            return map;
        }

        private static void BreakCycles(List<LauncherNode> nodes, Dictionary<string, LauncherNode> byId)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
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
                    LauncherNode parent;
                    if (!byId.TryGetValue(p, out parent))
                    {
                        n.ParentUid = "";
                        break;
                    }
                    p = parent.ParentUid ?? "";
                }
            }
        }

        private static Dictionary<string, List<LauncherNode>> ChildrenMap(IList<LauncherNode> nodes)
        {
            var map = new Dictionary<string, List<LauncherNode>>(StringComparer.Ordinal);
            if (nodes == null) return map;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n == null) continue;
                string p = n.ParentUid ?? "";
                List<LauncherNode> list;
                if (!map.TryGetValue(p, out list))
                {
                    list = new List<LauncherNode>();
                    map[p] = list;
                }
                list.Add(n);
            }
            return map;
        }

        private static List<LauncherNode> ChildrenOf(Dictionary<string, List<LauncherNode>> children, string parentUid)
        {
            List<LauncherNode> list;
            if (!children.TryGetValue(parentUid ?? "", out list) || list == null)
                return new List<LauncherNode>();
            return new List<LauncherNode>(list);
        }

        private static void SortSiblings(List<LauncherNode> list)
        {
            list.Sort(CompareSiblings);
        }

        private static int NonSystemCount(List<LauncherNode> accepted)
        {
            int c = 0;
            for (int i = 0; i < accepted.Count; i++)
            {
                if (accepted[i] != null && !LauncherSystemCatalog.IsSystemUid(accepted[i].Uid))
                    c++;
            }
            return c;
        }

        private static int CompareSiblings(LauncherNode a, LauncherNode b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            bool aSys = a.Uid == LauncherSystemCatalog.FolderUid;
            bool bSys = b.Uid == LauncherSystemCatalog.FolderUid;
            if (aSys != bSys) return aSys ? -1 : 1;
            int pin = b.Pinned.CompareTo(a.Pinned);
            if (pin != 0) return pin;
            int cat = (b.IsCategory ? 1 : 0).CompareTo(a.IsCategory ? 1 : 0);
            if (cat != 0) return cat;
            int name = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            if (name != 0) return name;
            return string.Compare(a.Uid, b.Uid, StringComparison.Ordinal);
        }

        private static void CollectBounded(LauncherNode n, int depth, Dictionary<string, List<LauncherNode>> children,
            List<LauncherNode> accepted, HashSet<string> acceptedIds)
        {
            if (n == null) return;
            if (!LauncherSystemCatalog.IsSystemUid(n.Uid) && NonSystemCount(accepted) >= MaxNodes) return;
            if (depth >= MaxDepth) return;
            if (!acceptedIds.Add(n.Uid)) return;
            accepted.Add(n);
            if (!n.IsCategory) return;
            var kids = ChildrenOf(children, n.Uid);
            SortSiblings(kids);
            for (int i = 0; i < kids.Count; i++)
                CollectBounded(kids[i], depth + 1, children, accepted, acceptedIds);
        }

        private static HashSet<string> BuildInclude(List<LauncherNode> nodes, Dictionary<string, List<LauncherNode>> children, string query)
        {
            var include = new HashSet<string>(StringComparer.Ordinal);
            var byId = IndexByUid(nodes);
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (!Matches(n, query)) continue;
                include.Add(n.Uid);
                if (n.IsCategory)
                    CollectUids(n.Uid, children, include);
            }
            var extra = new List<string>(include);
            for (int i = 0; i < extra.Count; i++)
                AddAncestors(extra[i], byId, include);
            return include;
        }

        private static bool Matches(LauncherNode n, string query)
        {
            if (n == null) return false;
            if (LauncherSearch.Matches(query, n.Name)) return true;
            if (LauncherSearch.Matches(query, n.Target)) return true;
            if (LauncherSearch.Matches(query, LauncherTarget.KindLabel(n.Kind))) return true;
            return false;
        }

        private static void AddAncestors(string uid, Dictionary<string, LauncherNode> byId, HashSet<string> include)
        {
            LauncherNode n;
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

        private static void Walk(string parentUid, int depth, Dictionary<string, List<LauncherNode>> children,
            ICollection<string> collapsed, HashSet<string> include, List<LauncherFlatRow> result)
        {
            var kids = ChildrenOf(children, parentUid);
            SortSiblings(kids);
            for (int i = 0; i < kids.Count; i++)
            {
                var n = kids[i];
                if (include != null && !include.Contains(n.Uid)) continue;
                var visibleKids = VisibleChildren(children, n.Uid, include);
                bool hasChildren = n.IsCategory && visibleKids.Count > 0;
                bool expanded = hasChildren && (include != null || collapsed == null || !collapsed.Contains(n.Uid));
                result.Add(new LauncherFlatRow
                {
                    Node = n,
                    Depth = depth,
                    HasChildren = hasChildren,
                    Expanded = expanded
                });
                if (n.IsCategory && expanded)
                    Walk(n.Uid, depth + 1, children, collapsed, include, result);
            }
        }

        private static void WalkCategories(string parentUid, int depth, Dictionary<string, List<LauncherNode>> children, List<LauncherFlatRow> result)
        {
            var kids = ChildrenOf(children, parentUid);
            SortSiblings(kids);
            for (int i = 0; i < kids.Count; i++)
            {
                var n = kids[i];
                if (!n.IsCategory) continue;
                if (LauncherSystemCatalog.IsSystemUid(n.Uid)) continue;
                var catKids = ChildrenOf(children, n.Uid);
                bool has = false;
                for (int j = 0; j < catKids.Count; j++)
                {
                    if (catKids[j].IsCategory) { has = true; break; }
                }
                result.Add(new LauncherFlatRow
                {
                    Node = n,
                    Depth = depth,
                    HasChildren = has,
                    Expanded = true
                });
                WalkCategories(n.Uid, depth + 1, children, result);
            }
        }

        private static List<LauncherNode> VisibleChildren(Dictionary<string, List<LauncherNode>> children, string parentUid, HashSet<string> include)
        {
            var kids = ChildrenOf(children, parentUid);
            if (include == null) return kids;
            var vis = new List<LauncherNode>();
            for (int i = 0; i < kids.Count; i++)
            {
                if (include.Contains(kids[i].Uid))
                    vis.Add(kids[i]);
            }
            return vis;
        }

        private static int CountDescLocked(string uid, Dictionary<string, List<LauncherNode>> children)
        {
            var kids = ChildrenOf(children, uid);
            int n = kids.Count;
            for (int i = 0; i < kids.Count; i++)
            {
                if (kids[i].IsCategory)
                    n += CountDescLocked(kids[i].Uid, children);
            }
            return n;
        }

        private static void CollectUids(string uid, Dictionary<string, List<LauncherNode>> children, ICollection<string> ids)
        {
            var kids = ChildrenOf(children, uid);
            for (int i = 0; i < kids.Count; i++)
            {
                ids.Add(kids[i].Uid);
                if (kids[i].IsCategory)
                    CollectUids(kids[i].Uid, children, ids);
            }
        }

        private static bool NameTaken(IList<LauncherNode> nodes, string parentUid, string name, string exceptUid)
        {
            string parent = parentUid ?? "";
            if (nodes == null) return false;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n == null) continue;
                if ((n.ParentUid ?? "") != parent) continue;
                if (!string.IsNullOrEmpty(exceptUid) && n.Uid == exceptUid) continue;
                if (string.Equals(n.Name, name, StringComparison.CurrentCultureIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void DuplicateWalk(LauncherNode src, string parentUid, string name,
            Dictionary<string, List<LauncherNode>> children, List<LauncherNode> result)
        {
            var now = DateTime.Now;
            var copy = Clone(src);
            copy.Uid = Guid.NewGuid().ToString("N");
            copy.ParentUid = parentUid ?? "";
            copy.Name = name;
            copy.Pinned = false;
            copy.CreatedAt = now;
            copy.UpdatedAt = now;
            result.Add(copy);
            if (!src.IsCategory) return;
            var kids = ChildrenOf(children, src.Uid);
            SortSiblings(kids);
            for (int i = 0; i < kids.Count; i++)
                DuplicateWalk(kids[i], copy.Uid, kids[i].Name, children, result);
        }
    }
}
