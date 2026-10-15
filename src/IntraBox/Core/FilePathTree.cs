using System;
using System.Collections.Generic;
using System.IO;

namespace IntraBox.Core
{
    /// <summary>
    /// 把若干完整路径收成目录树。目录在前、同层按名称排序。不访问磁盘。
    /// </summary>
    public static class FilePathTree
    {
        public sealed class Leaf
        {
            public string FullPath { get; set; }
            public string Detail { get; set; }
            public object Payload { get; set; }
        }

        public sealed class Node
        {
            public Node()
            {
                Children = new List<Node>();
            }

            public string Name { get; set; }
            public string FullPath { get; set; }
            public string Detail { get; set; }
            public bool IsFile { get; set; }
            public object Payload { get; set; }
            public List<Node> Children { get; private set; }
        }

        public static List<Node> Build(string root, IList<Leaf> leaves)
        {
            var top = new List<Node>();
            if (leaves == null) return top;
            string rootFull = Normalize(root);
            for (int i = 0; i < leaves.Count; i++)
            {
                Leaf leaf = leaves[i];
                if (leaf == null || string.IsNullOrWhiteSpace(leaf.FullPath)) continue;
                string full = Normalize(leaf.FullPath);
                string rel = Relative(rootFull, full);
                string[] parts = Split(rel);
                if (parts.Length == 0) continue;
                List<Node> level = top;
                string acc = "";
                for (int p = 0; p < parts.Length; p++)
                {
                    bool file = p == parts.Length - 1;
                    acc = acc.Length == 0 ? parts[p] : acc + "\\" + parts[p];
                    Node existing;
                    if (TryFind(level, parts[p], file, out existing))
                    {
                        if (!file) level = existing.Children;
                        continue;
                    }
                    var node = new Node();
                    node.Name = parts[p];
                    node.IsFile = file;
                    node.FullPath = file ? full : Combine(rootFull, acc);
                    node.Detail = file ? (leaf.Detail ?? "") : "";
                    node.Payload = file ? leaf.Payload : null;
                    level.Add(node);
                    if (!file) level = node.Children;
                }
            }
            SortRecursive(top);
            return top;
        }

        private static bool TryFind(List<Node> level, string name, bool file, out Node found)
        {
            for (int i = 0; i < level.Count; i++)
            {
                Node n = level[i];
                if (n.IsFile != file) continue;
                if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = n;
                    return true;
                }
            }
            found = null;
            return false;
        }

        private static void SortRecursive(List<Node> nodes)
        {
            nodes.Sort(Compare);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].IsFile) SortRecursive(nodes[i].Children);
            }
        }

        private static int Compare(Node a, Node b)
        {
            if (a.IsFile != b.IsFile) return a.IsFile ? 1 : -1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static string[] Split(string rel)
        {
            string[] raw = rel.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return raw;
        }

        private static string Relative(string rootFull, string full)
        {
            if (string.IsNullOrEmpty(rootFull)) return full;
            if (string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(full);
            string prefix = rootFull.EndsWith("\\", StringComparison.Ordinal) ? rootFull : rootFull + "\\";
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return full.Substring(prefix.Length);
            return full;
        }

        private static string Combine(string rootFull, string acc)
        {
            if (string.IsNullOrEmpty(rootFull)) return acc;
            return rootFull.TrimEnd('\\') + "\\" + acc;
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            return path.Trim().TrimEnd('\\', '/');
        }
    }
}
