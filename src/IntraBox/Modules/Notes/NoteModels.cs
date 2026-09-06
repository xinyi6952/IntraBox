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
        public bool Pinned { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public static NoteItem CreateNew(int kind, string title)
        {
            var now = DateTime.Now;
            return new NoteItem
            {
                Uid = Guid.NewGuid().ToString("N"),
                Title = title,
                Kind = kind == NoteKind.Markdown ? NoteKind.Markdown : NoteKind.Rich,
                CreatedAt = now,
                UpdatedAt = now
            };
        }
    }
}
