using System;

namespace IntraBox.Core
{
    /// <summary>启动器树节点：目录或收藏。</summary>
    public sealed class LauncherNode
    {
        public string Uid { get; set; }
        public string Type { get; set; }
        public string ParentUid { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Target { get; set; }
        public string OpenWith { get; set; }
        /// <summary>程序打开前是否二次确认。缺省（null）视为 true，兼容旧数据。</summary>
        public bool? ConfirmOpen { get; set; }
        public bool Pinned { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public bool IsCategory
        {
            get { return Type == LauncherTree.TypeCategory; }
        }
    }

    /// <summary>旧版 launcher.json 的扁平收藏（仅反序列化迁移用）。</summary>
    public sealed class LauncherFavorite
    {
        public string Uid { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Target { get; set; }
        public string Category { get; set; }
        public bool Pinned { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>树表展平后的一行。</summary>
    public sealed class LauncherFlatRow
    {
        public LauncherNode Node { get; set; }
        public int Depth { get; set; }
        public bool HasChildren { get; set; }
        public bool Expanded { get; set; }
    }
}
