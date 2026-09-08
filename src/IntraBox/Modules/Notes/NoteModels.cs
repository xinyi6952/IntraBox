using System;
using System.Collections.Generic;

namespace IntraBox.Modules.Notes
{
    public static class NoteKind
    {
        public const int Rich = 0;
        public const int Markdown = 1;

        public static string Label(int kind)
        {
            return kind == Markdown ? "Markdown" : "普通";
        }

        public static string DefaultTitle(int kind)
        {
            return kind == Markdown ? "无标题Markdown" : "无标题笔记";
        }

        public const string DefaultFolderTitle = "无标题目录";
    }

    public static class NoteNodeType
    {
        public const string Note = "note";
        public const string Folder = "folder";
    }

    public sealed class NoteIndexFile
    {
        public int Version { get; set; }
        public bool SampleSeeded { get; set; }
        public int SampleRev { get; set; }
        public List<NoteItem> Items { get; set; }
    }

    public sealed class NoteItem
    {
        public string Uid { get; set; }
        public string Title { get; set; }
        public int Kind { get; set; }
        /// <summary>note 或 folder；缺省视为笔记。</summary>
        public string Type { get; set; }
        public string ParentUid { get; set; }
        public bool Pinned { get; set; }
        /// <summary>锁定后只能查看，不能编辑或删除；即使勾选自动保存也不写入。</summary>
        public bool ReadOnly { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public bool IsFolder
        {
            get { return Type == NoteNodeType.Folder; }
        }

        public static NoteItem CreateNew(int kind, string title)
        {
            var now = DateTime.Now;
            return new NoteItem
            {
                Uid = Guid.NewGuid().ToString("N"),
                Title = title,
                Kind = kind == NoteKind.Markdown ? NoteKind.Markdown : NoteKind.Rich,
                Type = NoteNodeType.Note,
                ParentUid = "",
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        public static NoteItem CreateFolder(string title, string parentUid)
        {
            var now = DateTime.Now;
            return new NoteItem
            {
                Uid = Guid.NewGuid().ToString("N"),
                Title = title,
                Kind = NoteKind.Rich,
                Type = NoteNodeType.Folder,
                ParentUid = parentUid ?? "",
                CreatedAt = now,
                UpdatedAt = now
            };
        }
    }

    /// <summary>树表展平后的一行。</summary>
    public sealed class NoteFlatRow
    {
        public NoteItem Item { get; set; }
        public int Depth { get; set; }
        public bool HasChildren { get; set; }
        public bool Expanded { get; set; }
    }
}
