using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using IntraBox.Core;
using Newtonsoft.Json;

namespace IntraBox.Modules.FileOrganize
{
    /// <summary>桌面整理的持久化状态。独立于 history.json 的 LRU，避免规则被挤掉。</summary>
    public sealed class FileOrganizeState
    {
        public string Dir { get; set; }
        public bool Sub { get; set; }
        public List<OrganizeRule> Rules { get; set; }
    }

    /// <summary>读写数据根目录下的 fileorganize.json。撤销记录只写 fileorganize-undo.json。</summary>
    public static class FileOrganizeStore
    {
        private static readonly object _sync = new object();
        private static Timer _diskTimer;
        private static string _pendingPath;
        private static FileOrganizeState _pending;
        private static int _generation;
        private static int _inflight;
        private static bool _flushing;
        private static List<OrganizePlanItem> _lastUndo;

        public static string DefaultPath
        {
            get { return DataPaths.FileOrganizeJson; }
        }

        public static string UndoPathFor(string statePath)
        {
            string dir = string.IsNullOrEmpty(statePath)
                ? DataPaths.Root
                : Path.GetDirectoryName(statePath);
            if (string.IsNullOrEmpty(dir))
                dir = DataPaths.Root;
            return Path.Combine(dir, "fileorganize-undo.json");
        }

        public static FileOrganizeState Load(string path)
        {
            FileOrganizeState saved = null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(json))
                        saved = JsonConvert.DeserializeObject<FileOrganizeState>(json);
                }
                catch
                {
                    saved = null;
                }
            }

            var undo = ReadUndoFile(UndoPathFor(path ?? DefaultPath));
            lock (_sync)
            {
                _lastUndo = undo.Count > 0 ? CopyMoves(undo) : null;
            }
            return saved;
        }

        public static void SetLastUndo(IList<OrganizePlanItem> moves)
        {
            List<OrganizePlanItem> copy;
            string undoPath;
            lock (_sync)
            {
                _lastUndo = CopyMoves(moves);
                copy = CopyMoves(_lastUndo);
                undoPath = UndoPathFor(_pendingPath ?? DefaultPath);
            }
            WriteUndoFile(undoPath, copy);
        }

        public static void ClearLastUndo()
        {
            string undoPath;
            lock (_sync)
            {
                _lastUndo = null;
                undoPath = UndoPathFor(_pendingPath ?? DefaultPath);
            }
            WriteUndoFile(undoPath, new List<OrganizePlanItem>());
        }

        public static List<OrganizePlanItem> CopyLastUndo()
        {
            lock (_sync)
            {
                return CopyMoves(_lastUndo);
            }
        }

        public static string FormatUndoSummary(IList<OrganizePlanItem> moves, int maxItems)
        {
            if (moves == null || moves.Count == 0) return "";
            if (maxItems < 1) maxItems = 8;
            var sb = new StringBuilder();
            sb.Append("将把以下 ").Append(moves.Count).Append(" 个文件移回整理前的位置：");
            int show = moves.Count < maxItems ? moves.Count : maxItems;
            for (int i = 0; i < show; i++)
            {
                var m = moves[i];
                if (m == null) continue;
                sb.Append("\n\n").Append(i + 1).Append(". ").Append(string.IsNullOrEmpty(m.Name) ? "(未命名)" : m.Name);
                sb.Append("\n   现位置：").Append(string.IsNullOrEmpty(m.ToPath) ? "(未知)" : m.ToPath);
                sb.Append("\n   移回：").Append(string.IsNullOrEmpty(m.FromPath) ? "(未知)" : m.FromPath);
            }
            if (moves.Count > show)
                sb.Append("\n\n……还有 ").Append(moves.Count - show).Append(" 个文件未列出");
            return sb.ToString();
        }

        public static void Save(string path, FileOrganizeState state)
        {
            if (string.IsNullOrEmpty(path) || state == null) return;
            lock (_sync)
            {
                _pendingPath = path;
                _pending = Clone(state);
                _generation++;
                int ms = AppSettings.CurrentHistoryPersistDelayMs();
                if (_diskTimer == null)
                    _diskTimer = new Timer(DiskTimerCallback, null, ms, Timeout.Infinite);
                else
                    _diskTimer.Change(ms, Timeout.Infinite);
            }
        }

        public static void Flush()
        {
            string path;
            FileOrganizeState pending;
            lock (_sync)
            {
                _flushing = true;
                if (_diskTimer != null)
                    _diskTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            WaitInflight(2000);
            lock (_sync)
            {
                path = _pendingPath;
                pending = _pending;
            }
            WriteFile(path, pending);
            lock (_sync)
            {
                _flushing = false;
            }
        }

        private static void DiskTimerCallback(object state)
        {
            ThreadPool.QueueUserWorkItem(BackgroundPersist);
        }

        private static void BackgroundPersist(object state)
        {
            string path = null;
            FileOrganizeState pending = null;
            int gen = 0;
            lock (_sync)
            {
                if (_flushing) return;
                path = _pendingPath;
                pending = _pending;
                gen = _generation;
                _inflight++;
            }
            try
            {
                WriteFile(path, pending);
            }
            finally
            {
                lock (_sync)
                {
                    _inflight--;
                    if (!_flushing && gen != _generation)
                        ThreadPool.QueueUserWorkItem(BackgroundPersist);
                }
            }
        }

        private static void WaitInflight(int timeoutMs)
        {
            var start = Environment.TickCount;
            while (true)
            {
                lock (_sync)
                {
                    if (_inflight <= 0) return;
                }
                if (unchecked(Environment.TickCount - start) >= timeoutMs) return;
                Thread.Sleep(10);
            }
        }

        private static FileOrganizeState Clone(FileOrganizeState state)
        {
            var copy = new FileOrganizeState
            {
                Dir = state.Dir,
                Sub = state.Sub,
                Rules = new List<OrganizeRule>()
            };
            if (state.Rules == null) return copy;
            for (int i = 0; i < state.Rules.Count; i++)
            {
                var r = state.Rules[i];
                if (r == null) continue;
                copy.Rules.Add(new OrganizeRule
                {
                    Kind = r.Kind,
                    Pattern = r.Pattern,
                    TargetTemplate = r.TargetTemplate,
                    Enabled = r.Enabled
                });
            }
            return copy;
        }

        private static List<OrganizePlanItem> CopyMoves(IList<OrganizePlanItem> moves)
        {
            var list = new List<OrganizePlanItem>();
            if (moves == null) return list;
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                if (m == null) continue;
                list.Add(new OrganizePlanItem
                {
                    Name = m.Name,
                    FromPath = m.FromPath,
                    ToPath = m.ToPath,
                    Folder = m.Folder,
                    Note = m.Note
                });
            }
            return list;
        }

        private static void WriteFile(string path, FileOrganizeState state)
        {
            if (string.IsNullOrEmpty(path) || state == null) return;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(state, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        private static List<OrganizePlanItem> ReadUndoFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new List<OrganizePlanItem>();
            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return new List<OrganizePlanItem>();
                var list = JsonConvert.DeserializeObject<List<OrganizePlanItem>>(json);
                return CopyMoves(list);
            }
            catch
            {
                return new List<OrganizePlanItem>();
            }
        }

        private static void WriteUndoFile(string path, IList<OrganizePlanItem> moves)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(CopyMoves(moves), Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}
