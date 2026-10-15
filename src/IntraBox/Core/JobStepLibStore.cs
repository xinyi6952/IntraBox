using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace IntraBox.Core
{
    /// <summary>步骤库：jobs/library.json。示例固定 uid 只读；编排挑选时拷快照。</summary>
    public static class JobStepLibStore
    {
        public const int FormatVersion = 1;

        private static readonly object Sync = new object();
        private static List<JobStep> _steps = new List<JobStep>();
        private static bool _loaded;
        private static bool _writeBlocked;
        private static int _sampleRev;

        public static List<JobStep> Snapshot()
        {
            EnsureLoaded();
            lock (Sync)
            {
                var list = new List<JobStep>(_steps.Count);
                for (int i = 0; i < _steps.Count; i++)
                    list.Add(JobFlowStore.CloneStep(_steps[i]));
                return list;
            }
        }

        public static JobStep Find(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            EnsureLoaded();
            lock (Sync)
            {
                for (int i = 0; i < _steps.Count; i++)
                {
                    if (_steps[i].Uid == uid)
                        return JobFlowStore.CloneStep(_steps[i]);
                }
            }
            return null;
        }

        public static void Reload()
        {
            lock (Sync)
            {
                _loaded = false;
                LoadLocked();
            }
        }

        public static bool Save(JobStep step, out string error)
        {
            error = JobHeaderHelper.ValidateStep(step);
            if (error != null) return false;
            if (JobSamples.IsFixedStepUid(step.Uid))
            {
                error = "【示例】步骤只读，不能保存。请新建副本。";
                return false;
            }
            if (string.IsNullOrEmpty(step.Uid))
                step.Uid = Guid.NewGuid().ToString("N");
            step.Name = step.Name.Trim();
            JobHeaderHelper.Normalize(step);
            EnsureLoaded();
            lock (Sync)
            {
                if (_writeBlocked)
                {
                    error = "步骤库损坏且无法隔离，暂不能保存";
                    return false;
                }
                int idx = IndexOfLocked(step.Uid);
                var copy = JobFlowStore.CloneStep(step);
                if (idx >= 0) _steps[idx] = copy;
                else _steps.Add(copy);
                try
                {
                    WriteLocked();
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.RootMessage();
                    return false;
                }
            }
        }

        public static bool Delete(string uid, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(uid))
            {
                error = "无效编号";
                return false;
            }
            if (JobSamples.IsFixedStepUid(uid))
            {
                error = "【示例】步骤不可删除";
                return false;
            }
            EnsureLoaded();
            lock (Sync)
            {
                if (_writeBlocked)
                {
                    error = "步骤库损坏且无法隔离，暂不能删除";
                    return false;
                }
                int idx = IndexOfLocked(uid);
                if (idx < 0)
                {
                    error = "找不到该步骤";
                    return false;
                }
                _steps.RemoveAt(idx);
                try
                {
                    WriteLocked();
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.RootMessage();
                    return false;
                }
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (Sync)
            {
                if (_loaded) return;
                LoadLocked();
            }
        }

        private static void LoadLocked()
        {
            _steps = new List<JobStep>();
            _writeBlocked = false;
            _loaded = true;
            _sampleRev = 0;
            string path = DataPaths.JobsLibraryJson;
            bool loadFailed = false;
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var file = JsonConvert.DeserializeObject<JobLibFile>(json);
                    if (file != null)
                    {
                        _sampleRev = file.SampleRev;
                        if (file.Steps != null)
                        {
                            for (int i = 0; i < file.Steps.Count; i++)
                            {
                                var s = file.Steps[i];
                                if (s == null || string.IsNullOrEmpty(s.Uid)) continue;
                                if (s.Extracts == null) s.Extracts = new List<JobExtract>();
                                JobHeaderHelper.Normalize(s);
                                _steps.Add(s);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                loadFailed = true;
                _steps = new List<JobStep>();
                _sampleRev = 0;
                CompatDegrade.Once("jobstep-lib-corrupt", "步骤库数据损坏，已尝试备份", ex);
            }
            if (loadFailed)
            {
                if (!DataFileGuard.TryQuarantine(path))
                    _writeBlocked = true;
                else
                {
                    SeedSamplesLocked();
                    _sampleRev = JobSamples.CurrentSampleRev;
                    try { WriteLocked(); } catch { }
                }
                return;
            }
            if (_sampleRev < JobSamples.CurrentSampleRev)
            {
                RefreshSamplesLocked();
                _sampleRev = JobSamples.CurrentSampleRev;
                try { WriteLocked(); } catch { }
            }
        }

        private static void SeedSamplesLocked()
        {
            var stock = JobSamples.CreateLibrarySteps();
            for (int i = 0; i < stock.Count; i++)
            {
                if (IndexOfLocked(stock[i].Uid) < 0)
                    _steps.Insert(Math.Min(i, _steps.Count), JobFlowStore.CloneStep(stock[i]));
            }
        }

        private static void RefreshSamplesLocked()
        {
            var stock = JobSamples.CreateLibrarySteps();
            for (int i = 0; i < stock.Count; i++)
            {
                var next = JobFlowStore.CloneStep(stock[i]);
                int idx = IndexOfLocked(next.Uid);
                if (idx >= 0) _steps[idx] = next;
                else _steps.Insert(Math.Min(i, _steps.Count), next);
            }
        }

        private static void WriteLocked()
        {
            Directory.CreateDirectory(DataPaths.JobsDir);
            var file = new JobLibFile
            {
                FormatVersion = FormatVersion,
                SampleRev = _sampleRev,
                Steps = new List<JobStep>(_steps.Count)
            };
            for (int i = 0; i < _steps.Count; i++)
                file.Steps.Add(JobFlowStore.CloneStep(_steps[i]));
            string path = DataPaths.JobsLibraryJson;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(file, Formatting.Indented), new UTF8Encoding(false));
            File.Copy(tmp, path, true);
            try { File.Delete(tmp); } catch { }
        }

        private static int IndexOfLocked(string uid)
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                if (_steps[i].Uid == uid) return i;
            }
            return -1;
        }
    }

    public sealed class JobLibFile
    {
        public int FormatVersion { get; set; }
        public int SampleRev { get; set; }
        public List<JobStep> Steps { get; set; }
    }
}
