using System;
using System.Collections.Generic;
using System.IO;

namespace IntraBox.Core
{
    /// <summary>
    /// 启动器固定「系统」目录：本机 System32 / 桌面存在才出现。
    /// 不能改名、移动、删除；置顶单独记在 launcher.json。
    /// </summary>
    public static class LauncherSystemCatalog
    {
        public const string FolderUid = "sys:folder";
        public const string FolderName = "系统";
        public const string UidPrefix = "sys:";
        public const string NotepadId = "notepad";

        public delegate bool PathExists(string path);

        private sealed class Spec
        {
            public string Id;
            public string Name;
            public string Kind;
            public string FileName;
            public bool Desktop;
        }

        public static bool IsSystemUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            return uid.StartsWith(UidPrefix, StringComparison.Ordinal);
        }

        public static bool IsSystemFolder(string uid)
        {
            return uid == FolderUid;
        }

        public static bool IsSystemItem(string uid)
        {
            return IsSystemUid(uid) && uid != FolderUid;
        }

        public static bool IsNotepadItem(string uid)
        {
            return uid == ItemUid(NotepadId);
        }

        public static string ItemUid(string id)
        {
            return UidPrefix + (id ?? "");
        }

        public static List<LauncherNode> BuildCurrent(ICollection<string> pinnedIds)
        {
            return Build(
                pinnedIds,
                Environment.SystemDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                File.Exists,
                Directory.Exists);
        }

        /// <summary>按传入路径与存在性探测生成节点；全部缺失则返回空列表（不建空目录）。</summary>
        public static List<LauncherNode> Build(
            ICollection<string> pinnedIds,
            string systemDirectory,
            string desktopDirectory,
            PathExists fileExists,
            PathExists directoryExists)
        {
            var pinned = new HashSet<string>(StringComparer.Ordinal);
            if (pinnedIds != null)
            {
                foreach (string id in pinnedIds)
                {
                    if (IsSystemItem(id)) pinned.Add(id);
                }
            }

            var kids = new List<LauncherNode>();
            var specs = Specs();
            var now = new DateTime(2020, 1, 1);
            for (int i = 0; i < specs.Length; i++)
            {
                Spec s = specs[i];
                string target;
                bool ok;
                if (s.Desktop)
                {
                    target = desktopDirectory ?? "";
                    ok = target.Length > 0 && directoryExists != null && directoryExists(target);
                }
                else
                {
                    if (string.IsNullOrEmpty(systemDirectory) || string.IsNullOrEmpty(s.FileName))
                        continue;
                    target = Path.Combine(systemDirectory, s.FileName);
                    ok = fileExists != null && fileExists(target);
                }
                if (!ok) continue;
                string uid = ItemUid(s.Id);
                kids.Add(new LauncherNode
                {
                    Uid = uid,
                    Type = LauncherTree.TypeFavorite,
                    ParentUid = FolderUid,
                    Name = s.Name,
                    Kind = s.Kind,
                    Target = target,
                    OpenWith = "",
                    ConfirmOpen = false,
                    Pinned = pinned.Contains(uid),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            var list = new List<LauncherNode>();
            if (kids.Count == 0) return list;
            list.Add(LauncherTree.NewCategory(FolderUid, "", FolderName, false, now, now));
            for (int i = 0; i < kids.Count; i++)
                list.Add(kids[i]);
            return list;
        }

        private static Spec[] Specs()
        {
            return new[]
            {
                FileSpec("calc", "计算器", LauncherTarget.KindApp, "calc.exe"),
                FileSpec("control", "控制面板", LauncherTarget.KindApp, "control.exe"),
                FileSpec("taskmgr", "任务管理器", LauncherTarget.KindApp, "taskmgr.exe"),
                FileSpec("mstsc", "远程桌面", LauncherTarget.KindApp, "mstsc.exe"),
                FileSpec(NotepadId, "记事本", LauncherTarget.KindApp, "notepad.exe"),
                FileSpec("mspaint", "画图", LauncherTarget.KindApp, "mspaint.exe"),
                FileSpec("ncpa", "网络连接", LauncherTarget.KindFile, "ncpa.cpl"),
                FileSpec("appwiz", "程序和功能", LauncherTarget.KindFile, "appwiz.cpl"),
                FileSpec("sysdm", "系统属性", LauncherTarget.KindFile, "sysdm.cpl"),
                FileSpec("devmgmt", "设备管理器", LauncherTarget.KindFile, "devmgmt.msc"),
                FileSpec("services", "服务", LauncherTarget.KindFile, "services.msc"),
                new Spec { Id = "desktop", Name = "桌面", Kind = LauncherTarget.KindFolder, Desktop = true }
            };
        }

        private static Spec FileSpec(string id, string name, string kind, string fileName)
        {
            return new Spec { Id = id, Name = name, Kind = kind, FileName = fileName };
        }
    }
}
