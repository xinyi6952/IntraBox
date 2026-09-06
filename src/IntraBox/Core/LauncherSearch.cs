using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>启动器一条可执行结果。目录在打开浮层时编一次，按键只做过滤排序。</summary>
    public sealed class LauncherHit
    {
        public const string KindTool = "tool";
        public const string KindFavorite = "favorite";
        public const string KindTodo = "todo";
        public const string KindNote = "note";
        public const string KindVault = "vault";

        public string Id { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Category { get; set; }
        public string Payload { get; set; }
        public bool Pinned { get; set; }

        public static string ToolId(string key)
        {
            return "tool:" + (key ?? "");
        }

        public static string FavoriteId(string uid)
        {
            return "fav:" + (uid ?? "");
        }

        public static string TodoId(string uid)
        {
            return "todo:" + (uid ?? "");
        }

        public static string NoteId(string uid)
        {
            return "note:" + (uid ?? "");
        }

        public static string VaultId(string uid)
        {
            return "vault:" + (uid ?? "");
        }
    }

    /// <summary>启动器匹配与排序（可测）。空查询不搜待办/笔记/分类，由调用方先筛候选。</summary>
    public static class LauncherSearch
    {
        public const int MaxResults = 50;
        public const int EmptyQueryToolCap = 20;

        public static int NameScore(string query, string title)
        {
            if (string.IsNullOrEmpty(title)) return 0;
            if (string.IsNullOrWhiteSpace(query)) return 1;
            string q = query.Trim();
            if (title.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 100;
            if (title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return 50;
            return 0;
        }

        public static bool Matches(string query, string title)
        {
            return NameScore(query, title) > 0;
        }

        public static int HitScore(string query, LauncherHit hit)
        {
            if (hit == null) return 0;
            int a = NameScore(query, hit.Title);
            int b = NameScore(query, hit.Category);
            return a >= b ? a : b;
        }

        /// <summary>有关键字：过滤标题或收藏分类后按 置顶 &gt; 最近 &gt; 名称开头 &gt; 包含 排序，最多 MaxResults。</summary>
        public static List<LauncherHit> Rank(IList<LauncherHit> candidates, string query, IList<string> recentIds)
        {
            var matched = new List<LauncherHit>();
            if (candidates == null) return matched;
            for (int i = 0; i < candidates.Count; i++)
            {
                var h = candidates[i];
                if (h == null) continue;
                if (HitScore(query, h) <= 0) continue;
                matched.Add(h);
            }
            matched.Sort(delegate (LauncherHit a, LauncherHit b)
            {
                int pa = a.Pinned ? 0 : 1;
                int pb = b.Pinned ? 0 : 1;
                if (pa != pb) return pa.CompareTo(pb);
                int ra = RecentRank(recentIds, a.Id);
                int rb = RecentRank(recentIds, b.Id);
                if (ra != rb) return ra.CompareTo(rb);
                int sa = HitScore(query, a);
                int sb = HitScore(query, b);
                if (sa != sb) return sb.CompareTo(sa);
                return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });
            if (matched.Count > MaxResults)
                matched.RemoveRange(MaxResults, matched.Count - MaxResults);
            return matched;
        }

        /// <summary>空查询：置顶收藏 → 最近 → 可见工具（可 cap），最多 MaxResults。</summary>
        public static List<LauncherHit> EmptyQuery(IList<LauncherHit> catalog, IList<string> recentIds, int toolCap)
        {
            var result = new List<LauncherHit>();
            if (catalog == null) return result;
            if (toolCap < 0) toolCap = EmptyQueryToolCap;
            var seen = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int i = 0; i < catalog.Count; i++)
            {
                var h = catalog[i];
                if (h == null || h.Kind != LauncherHit.KindFavorite || !h.Pinned) continue;
                AddUnique(result, seen, h);
            }
            if (recentIds != null)
            {
                for (int r = 0; r < recentIds.Count; r++)
                {
                    var hit = FindById(catalog, recentIds[r]);
                    if (hit == null) continue;
                    if (hit.Kind == LauncherHit.KindTodo || hit.Kind == LauncherHit.KindNote
                        || hit.Kind == LauncherHit.KindVault)
                        continue;
                    AddUnique(result, seen, hit);
                }
            }
            int tools = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                var h = catalog[i];
                if (h == null || h.Kind != LauncherHit.KindTool) continue;
                if (tools >= toolCap) break;
                if (AddUnique(result, seen, h)) tools++;
            }
            if (result.Count > MaxResults)
                result.RemoveRange(MaxResults, result.Count - MaxResults);
            return result;
        }

        private static int RecentRank(IList<string> recents, string id)
        {
            if (recents == null || string.IsNullOrEmpty(id)) return 10000;
            for (int i = 0; i < recents.Count; i++)
            {
                if (recents[i] == id) return i;
            }
            return 10000;
        }

        private static LauncherHit FindById(IList<LauncherHit> catalog, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < catalog.Count; i++)
            {
                var h = catalog[i];
                if (h != null && h.Id == id) return h;
            }
            return null;
        }

        private static bool AddUnique(List<LauncherHit> result, Dictionary<string, bool> seen, LauncherHit hit)
        {
            if (hit == null || string.IsNullOrEmpty(hit.Id)) return false;
            if (seen.ContainsKey(hit.Id)) return false;
            seen[hit.Id] = true;
            result.Add(hit);
            return true;
        }
    }
}
