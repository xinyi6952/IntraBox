using System;
using System.IO;

namespace IntraBox.Core
{
    /// <summary>
    /// 个人数据根目录。默认 %LOCALAPPDATA%\IntraBox\Data；
    /// 用户改过目录时程序夹旁 datapath.txt 指向自定义路径。
    /// 不扫描、不迁移 exe 目录里的旧 json。
    /// </summary>
    public static class DataPaths
    {
        public const string PointerFileName = "datapath.txt";
        public const int CurrentConfigVersion = 2;

        private static string _root;
        private static readonly object _sync = new object();

        public static string ProgramDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string PointerPath
        {
            get { return Path.Combine(ProgramDir, PointerFileName); }
        }

        public static string DefaultRoot
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "IntraBox", "Data");
            }
        }

        public static string Root
        {
            get
            {
                lock (_sync)
                {
                    if (_root == null) ResolveLocked();
                    return _root;
                }
            }
        }

        public static string ConfigJson { get { return Path.Combine(Root, "config.json"); } }
        public static string HistoryJson { get { return Path.Combine(Root, "history.json"); } }
        public static string FileOrganizeJson { get { return Path.Combine(Root, "fileorganize.json"); } }
        public static string FileOrganizeUndoJson { get { return Path.Combine(Root, "fileorganize-undo.json"); } }
        public static string ErrorLog { get { return Path.Combine(Root, "error.log"); } }
        public static string GcLog { get { return Path.Combine(Root, "gc.log"); } }
        public static string TodosDir { get { return Path.Combine(Root, "todos"); } }
        public static string TodosIndexJson { get { return Path.Combine(TodosDir, "index.json"); } }
        public static string NotesDir { get { return Path.Combine(Root, "notes"); } }
        public static string NotesIndexJson { get { return Path.Combine(NotesDir, "index.json"); } }
        public static string VaultDir { get { return Path.Combine(Root, "vault"); } }
        public static string VaultIndexJson { get { return Path.Combine(VaultDir, "index.json"); } }
        public static string LauncherJson { get { return Path.Combine(Root, "launcher.json"); } }

        public static bool IsDefaultRoot
        {
            get { return SamePath(Root, DefaultRoot); }
        }

        public static void Initialize()
        {
            lock (_sync)
            {
                ResolveLocked();
            }
        }

        /// <summary>切换到新根（调用方已复制完文件）。isDefault 为 true 时删除指针文件。</summary>
        public static void SetRoot(string path, bool isDefault)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("路径无效", "path");
            string full = Normalize(path);
            Directory.CreateDirectory(full);
            lock (_sync)
            {
                _root = full;
                if (isDefault)
                    DeletePointer();
                else
                    WritePointer(full);
            }
        }

        public static bool TryValidateNewRoot(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "请选择一个文件夹";
                return false;
            }
            string full;
            try
            {
                full = Normalize(path);
            }
            catch (Exception ex)
            {
                error = "路径无效：" + ex.Message;
                return false;
            }
            if (IsInsideProgramDir(full))
            {
                error = "不能把数据目录放在程序文件夹内，换免安装包时容易被覆盖。";
                return false;
            }
            try
            {
                Directory.CreateDirectory(full);
                string probe = Path.Combine(full, ".intrabox-write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                error = "目录不可写：" + ex.Message;
                return false;
            }
            return true;
        }

        public static bool IsInsideProgramDir(string path)
        {
            string prog = Normalize(ProgramDir);
            string full = Normalize(path);
            if (string.Equals(full, prog, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!prog.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                prog += Path.DirectorySeparatorChar;
            return full.StartsWith(prog, StringComparison.OrdinalIgnoreCase);
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把当前数据根复制到 destRoot（覆盖同名文件）。不含指针文件。</summary>
        public static void CopyRootTo(string destRoot)
        {
            string src = Root;
            string dest = Normalize(destRoot);
            Directory.CreateDirectory(dest);
            CopyFileIfExists(ConfigJson, Path.Combine(dest, "config.json"));
            CopyFileIfExists(HistoryJson, Path.Combine(dest, "history.json"));
            CopyFileIfExists(FileOrganizeJson, Path.Combine(dest, "fileorganize.json"));
            CopyFileIfExists(FileOrganizeUndoJson, Path.Combine(dest, "fileorganize-undo.json"));
            CopyFileIfExists(ErrorLog, Path.Combine(dest, "error.log"));
            CopyFileIfExists(GcLog, Path.Combine(dest, "gc.log"));
            CopyFileIfExists(LauncherJson, Path.Combine(dest, "launcher.json"));
            if (Directory.Exists(src))
            {
                string[] baks = Directory.GetFiles(src, "launcher.json*.bak");
                for (int i = 0; i < baks.Length; i++)
                    CopyFileIfExists(baks[i], Path.Combine(dest, Path.GetFileName(baks[i])));
            }
            string todosSrc = TodosDir;
            string todosDest = Path.Combine(dest, "todos");
            if (Directory.Exists(todosSrc))
                CopyDirectory(todosSrc, todosDest);
            string notesSrc = NotesDir;
            string notesDest = Path.Combine(dest, "notes");
            if (Directory.Exists(notesSrc))
                CopyDirectory(notesSrc, notesDest);
            string vaultSrc = VaultDir;
            string vaultDest = Path.Combine(dest, "vault");
            if (Directory.Exists(vaultSrc))
                CopyDirectory(vaultSrc, vaultDest);
        }

        public static string NoteItemDir(string uid)
        {
            return Path.Combine(NotesDir, uid);
        }

        public static string NoteBodyXamlPath(string uid)
        {
            return Path.Combine(NoteItemDir(uid), "body.xaml");
        }

        public static string NoteBodyMdPath(string uid)
        {
            return Path.Combine(NoteItemDir(uid), "body.md");
        }

        public static string NoteFilesDir(string uid)
        {
            return Path.Combine(NoteItemDir(uid), "files");
        }

        public static string VaultItemDir(string uid)
        {
            return Path.Combine(VaultDir, uid);
        }

        public static string VaultBodyPath(string uid)
        {
            return Path.Combine(VaultItemDir(uid), "body.json");
        }

        public static string TodoItemDir(string uid)
        {
            return Path.Combine(TodosDir, uid);
        }

        public static string TodoDetailsPath(string uid)
        {
            return Path.Combine(TodoItemDir(uid), "details.xaml");
        }

        public static string TodoFilesDir(string uid)
        {
            return Path.Combine(TodoItemDir(uid), "files");
        }

        private static void ResolveLocked()
        {
            string chosen = null;
            try
            {
                string pointer = PointerPath;
                if (File.Exists(pointer))
                {
                    string line = File.ReadAllText(pointer).Trim();
                    if (!string.IsNullOrEmpty(line))
                    {
                        string full = Normalize(line);
                        if (!IsInsideProgramDir(full))
                            chosen = full;
                    }
                }
            }
            catch
            {
                chosen = null;
            }
            if (string.IsNullOrEmpty(chosen))
                chosen = Normalize(DefaultRoot);
            Directory.CreateDirectory(chosen);
            Directory.CreateDirectory(Path.Combine(chosen, "todos"));
            Directory.CreateDirectory(Path.Combine(chosen, "notes"));
            Directory.CreateDirectory(Path.Combine(chosen, "vault"));
            _root = chosen;
        }

        private static void WritePointer(string full)
        {
            File.WriteAllText(PointerPath, full + Environment.NewLine);
        }

        private static void DeletePointer()
        {
            try
            {
                if (File.Exists(PointerPath))
                    File.Delete(PointerPath);
            }
            catch { }
        }

        private static string Normalize(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void CopyFileIfExists(string src, string dest)
        {
            if (!File.Exists(src)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(src, dest, true);
        }

        private static void CopyDirectory(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }
}
