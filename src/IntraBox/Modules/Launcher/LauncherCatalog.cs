using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Documents;
using System.Windows.Markup;
using IntraBox.Core;
using IntraBox.Modules.Notes;
using IntraBox.Modules.Todo;
using IntraBox.Modules.Vault;

namespace IntraBox.Modules.Launcher
{
    /// <summary>打开浮层时编一次目录：可见工具、收藏、待办标题、笔记标题、账号分类名（不含密码）。</summary>
    public static class LauncherCatalog
    {
        public static List<LauncherHit> Build()
        {
            var list = new List<LauncherHit>();
            AddTools(list);
            AddFavorites(list);
            AddTodos(list);
            AddNotes(list);
            AddVault(list);
            return list;
        }

        private static void AddTools(List<LauncherHit> list)
        {
            var tools = ToolVisibility.NavTools();
            for (int i = 0; i < tools.Count; i++)
            {
                var m = tools[i];
                if (m == null) continue;
                list.Add(new LauncherHit
                {
                    Id = LauncherHit.ToolId(m.Key),
                    Kind = LauncherHit.KindTool,
                    Title = m.DisplayName,
                    Subtitle = m.Category,
                    Payload = m.Key
                });
            }
            list.Add(new LauncherHit
            {
                Id = LauncherHit.ToolId(ToolVisibility.SettingsKey),
                Kind = LauncherHit.KindTool,
                Title = "设置",
                Subtitle = "系统",
                Payload = ToolVisibility.SettingsKey
            });
        }

        private static void AddFavorites(List<LauncherHit> list)
        {
            var nodes = LauncherStore.SnapshotNodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                var f = nodes[i];
                if (f == null || f.IsCategory) continue;
                string path = LauncherTree.ParentPath(nodes, f.ParentUid);
                string cat = path.Length == 0 ? LauncherTarget.Uncategorized : path;
                list.Add(new LauncherHit
                {
                    Id = LauncherHit.FavoriteId(f.Uid),
                    Kind = LauncherHit.KindFavorite,
                    Title = f.Name,
                    Category = cat,
                    Subtitle = cat + " · " + LauncherTarget.KindLabel(f.Kind) + " · " + f.Target,
                    Payload = f.Uid,
                    Pinned = f.Pinned
                });
            }
        }

        private static void AddTodos(List<LauncherHit> list)
        {
            var items = TodoStore.Snapshot();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) continue;
                string title = TodoTitle(it);
                list.Add(new LauncherHit
                {
                    Id = LauncherHit.TodoId(it.Uid),
                    Kind = LauncherHit.KindTodo,
                    Title = title,
                    Subtitle = it.Completed ? "已办" : "待办",
                    Payload = it.Uid
                });
            }
        }

        private static void AddNotes(List<LauncherHit> list)
        {
            var items = NoteStore.Snapshot();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null || it.IsFolder) continue;
                string title = string.IsNullOrWhiteSpace(it.Title) ? "无标题笔记" : it.Title;
                list.Add(new LauncherHit
                {
                    Id = LauncherHit.NoteId(it.Uid),
                    Kind = LauncherHit.KindNote,
                    Title = title,
                    Subtitle = NoteKind.Label(it.Kind) + "笔记",
                    Payload = it.Uid
                });
            }
        }

        private static void AddVault(List<LauncherHit> list)
        {
            var items = VaultStore.Snapshot();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) continue;
                string title = string.IsNullOrWhiteSpace(it.Title) ? "未命名分类" : it.Title;
                list.Add(new LauncherHit
                {
                    Id = LauncherHit.VaultId(it.Uid),
                    Kind = LauncherHit.KindVault,
                    Title = title,
                    Subtitle = "账号备忘",
                    Payload = it.Uid
                });
            }
        }

        private static string TodoTitle(TodoItem it)
        {
            try
            {
                string path = DataPaths.TodoDetailsPath(it.Uid);
                if (File.Exists(path))
                {
                    using (var fs = File.OpenRead(path))
                    {
                        var doc = XamlReader.Load(fs) as FlowDocument;
                        string plain = TodoRichText.ToPlain(doc);
                        string t = TodoRichText.ExtractTitle(plain);
                        if (!string.IsNullOrEmpty(t)) return t;
                    }
                }
            }
            catch
            {
            }
            if (!string.IsNullOrEmpty(it.Id)) return it.Id;
            return "待办";
        }
    }
}
