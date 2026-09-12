using System.Collections.Generic;
using Newtonsoft.Json;

namespace IntraBox.Core
{
    /// <summary>launcher.json 反序列化 DTO（含旧 Favorites，仅加载用）。</summary>
    public sealed class LauncherFileState
    {
        public int FormatVersion { get; set; }
        public List<LauncherNode> Nodes { get; set; }
        public List<LauncherFavorite> Favorites { get; set; }
        public List<string> Recents { get; set; }
        public List<string> SystemPinned { get; set; }
    }

    /// <summary>解析 launcher.json 的结果。Ok 为 false 表示 JSON 损坏，不应覆盖原文件。</summary>
    public sealed class LauncherLoadResult
    {
        public bool Ok { get; set; }
        public bool NeedsRewrite { get; set; }
        public List<LauncherNode> Nodes { get; set; }
        public List<string> Recents { get; set; }
        public List<string> SystemPinned { get; set; }
    }

    /// <summary>
    /// 启动器落盘格式：读旧扁平 Favorites / 缺 FormatVersion 的树，写出当前结构。
    /// 不碰磁盘，便于单测。
    /// </summary>
    public static class LauncherFile
    {
        public const int CurrentFormat = 1;

        private static readonly JsonSerializerSettings SaveSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static LauncherLoadResult TryLoad(string json)
        {
            return TryLoad(json, 30);
        }

        public static LauncherLoadResult TryLoad(string json, int maxRecents)
        {
            var result = new LauncherLoadResult
            {
                Nodes = new List<LauncherNode>(),
                Recents = new List<string>(),
                SystemPinned = new List<string>()
            };
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Ok = true;
                result.NeedsRewrite = true;
                return result;
            }
            LauncherFileState state;
            try
            {
                state = JsonConvert.DeserializeObject<LauncherFileState>(json);
            }
            catch
            {
                result.Ok = false;
                return result;
            }
            if (state == null)
            {
                result.Ok = true;
                result.NeedsRewrite = true;
                return result;
            }

            bool migrated = false;
            if (state.Nodes != null && state.Nodes.Count > 0)
                result.Nodes = LauncherTree.Sanitize(state.Nodes);
            else if (state.Favorites != null && state.Favorites.Count > 0)
            {
                result.Nodes = LauncherTree.MigrateFromFavorites(state.Favorites);
                migrated = true;
            }

            if (StripReserved(result.Nodes))
                migrated = true;

            result.Recents = NormalizeRecents(state.Recents, maxRecents);
            result.SystemPinned = NormalizePins(state.SystemPinned);

            bool leftoverFavorites = state.Favorites != null && state.Favorites.Count > 0
                && state.Nodes != null && state.Nodes.Count > 0;
            bool oldFormat = state.FormatVersion < CurrentFormat;
            result.Ok = true;
            result.NeedsRewrite = migrated || leftoverFavorites || oldFormat;
            return result;
        }

        /// <summary>按当前格式写出：FormatVersion + Nodes + Recents + SystemPinned，不含 Favorites。</summary>
        public static string Save(IList<LauncherNode> nodes, IList<string> recents, IList<string> systemPinned)
        {
            var state = new LauncherSaveState
            {
                FormatVersion = CurrentFormat,
                Nodes = nodes != null ? new List<LauncherNode>(nodes) : new List<LauncherNode>(),
                Recents = recents != null ? new List<string>(recents) : new List<string>(),
                SystemPinned = systemPinned != null ? new List<string>(systemPinned) : new List<string>()
            };
            return JsonConvert.SerializeObject(state, SaveSettings);
        }

        public static bool StripReserved(List<LauncherNode> nodes)
        {
            if (nodes == null) return false;
            bool changed = false;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                var n = nodes[i];
                if (n == null)
                {
                    nodes.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (LauncherSystemCatalog.IsSystemUid(n.Uid))
                {
                    nodes.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (LauncherSystemCatalog.IsSystemUid(n.ParentUid))
                {
                    n.ParentUid = "";
                    changed = true;
                }
            }
            return changed;
        }

        public static List<string> NormalizeRecents(IList<string> raw, int max)
        {
            var list = new List<string>();
            if (raw == null || max <= 0) return list;
            for (int i = 0; i < raw.Count && list.Count < max; i++)
            {
                string id = raw[i];
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (list.Contains(id)) continue;
                list.Add(id);
            }
            return list;
        }

        public static List<string> NormalizePins(IList<string> raw)
        {
            var list = new List<string>();
            if (raw == null) return list;
            for (int i = 0; i < raw.Count; i++)
            {
                string id = raw[i];
                if (!LauncherSystemCatalog.IsSystemItem(id)) continue;
                if (list.Contains(id)) continue;
                list.Add(id);
            }
            return list;
        }

        private sealed class LauncherSaveState
        {
            public int FormatVersion { get; set; }
            public List<LauncherNode> Nodes { get; set; }
            public List<string> Recents { get; set; }
            public List<string> SystemPinned { get; set; }
        }
    }
}
