using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace IntraBox.Core
{
    /// <summary>任务编排落盘：jobs/index.json；损坏隔离 .bak。</summary>
    public static class JobFlowStore
    {
        public const int FormatVersion = 2;

        private static readonly object Sync = new object();
        private static List<JobItem> _jobs = new List<JobItem>();
        private static bool _loaded;
        private static bool _writeBlocked;
        private static bool _sampleSeeded;
        private static int _sampleRev;

        public static List<JobItem> Snapshot()
        {
            EnsureLoaded();
            lock (Sync)
            {
                var list = new List<JobItem>(_jobs.Count);
                for (int i = 0; i < _jobs.Count; i++)
                    list.Add(Clone(_jobs[i]));
                return list;
            }
        }

        public static JobItem Find(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            EnsureLoaded();
            lock (Sync)
            {
                for (int i = 0; i < _jobs.Count; i++)
                {
                    if (_jobs[i].Uid == uid)
                        return Clone(_jobs[i]);
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

        public static bool Save(JobItem job, out string error)
        {
            error = null;
            if (job == null)
            {
                error = "编排为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(job.Name))
            {
                error = "请填写名称";
                return false;
            }
            if (JobSamples.IsFixedSampleUid(job.Uid))
            {
                error = "【示例】只读，不能保存";
                return false;
            }
            if (JobLimits.IsStepOverflow(job))
            {
                error = "步骤超过 " + JobLimits.MaxStepsPerJob + " 个，只读不能保存。请拆成多条编排或删减步骤后另存。";
                return false;
            }
            if (string.IsNullOrEmpty(job.Uid))
                job.Uid = Guid.NewGuid().ToString("N");
            job.Name = job.Name.Trim();
            if (job.Remarks == null) job.Remarks = "";
            job.SyncEnabledFromStatus();
            job.ModifiedAt = DateTime.Now;
            if (job.CreatedAt == default(DateTime))
                job.CreatedAt = job.ModifiedAt;
            if (job.Params == null) job.Params = new List<JobParam>();
            if (job.Steps == null) job.Steps = new List<JobStep>();
            if (job.Trigger == null) job.Trigger = JobTrigger.Manual();

            EnsureLoaded();
            lock (Sync)
            {
                if (_writeBlocked)
                {
                    error = "编排数据损坏且无法隔离，暂不能保存";
                    return false;
                }
                int idx = IndexOfLocked(job.Uid);
                var copy = Clone(job);
                if (idx >= 0) _jobs[idx] = copy;
                else _jobs.Add(copy);
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
            if (JobSamples.IsFixedSampleUid(uid))
            {
                error = "【示例】不可删除";
                return false;
            }
            EnsureLoaded();
            lock (Sync)
            {
                if (_writeBlocked)
                {
                    error = "编排数据损坏且无法隔离，暂不能删除";
                    return false;
                }
                int idx = IndexOfLocked(uid);
                if (idx < 0)
                {
                    error = "找不到该编排";
                    return false;
                }
                _jobs.RemoveAt(idx);
                try
                {
                    WriteLocked();
                    try { JobSecretStore.Clear(uid, out _); } catch { }
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.RootMessage();
                    return false;
                }
            }
        }

        public static bool SetStatus(string uid, JobStatus status, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(uid))
            {
                error = "无效编号";
                return false;
            }
            if (JobSamples.IsFixedSampleUid(uid))
            {
                error = "【示例】不可修改状态";
                return false;
            }
            EnsureLoaded();
            lock (Sync)
            {
                if (_writeBlocked)
                {
                    error = "编排数据损坏且无法隔离，暂不能保存";
                    return false;
                }
                int idx = IndexOfLocked(uid);
                if (idx < 0)
                {
                    error = "找不到该编排";
                    return false;
                }
                if (JobLimits.IsStepOverflow(_jobs[idx]))
                {
                    error = "步骤超过 " + JobLimits.MaxStepsPerJob + " 个，只读不能改状态。";
                    return false;
                }
                _jobs[idx].Status = status;
                _jobs[idx].SyncEnabledFromStatus();
                _jobs[idx].ModifiedAt = DateTime.Now;
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

        public static void UpdateRunResult(string uid, JobRunResult result, bool fromAuto)
        {
            if (string.IsNullOrEmpty(uid) || result == null) return;
            EnsureLoaded();
            lock (Sync)
            {
                int idx = IndexOfLocked(uid);
                if (idx < 0) return;
                var job = _jobs[idx];
                job.LastRunAt = DateTime.Now;
                job.LastRunOk = result.Ok;
                job.LastRunSummary = JobVarHelper.Truncate(result.Message ?? "", 120);
                job.RunCount = job.RunCount < 0 ? 1 : job.RunCount + 1;
                if (fromAuto)
                    job.LastAutoAttemptAt = job.LastRunAt;
                if (!fromAuto && result.Ok)
                    job.ManualSucceeded = true;
                job.ModifiedAt = DateTime.Now;
                try { WriteLocked(); }
                catch (Exception ex) { AppLog.Error("编排运行结果写回失败：" + uid, ex); }
            }
            try { AppendRunLog(uid, result); }
            catch (Exception ex) { AppLog.Error("编排运行日志写入失败：" + uid, ex); }
        }

        public static void RecordProbePass(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            EnsureLoaded();
            lock (Sync)
            {
                int idx = IndexOfLocked(uid);
                if (idx < 0) return;
                _jobs[idx].TestPassAt = DateTime.Now;
                _jobs[idx].ModifiedAt = DateTime.Now;
                try { WriteLocked(); }
                catch { }
            }
        }

        public static void MarkAutoAttempt(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            EnsureLoaded();
            lock (Sync)
            {
                int idx = IndexOfLocked(uid);
                if (idx < 0) return;
                _jobs[idx].LastAutoAttemptAt = DateTime.Now;
                try { WriteLocked(); }
                catch { }
            }
        }

        public static string RunLogPath(string uid)
        {
            return Path.Combine(DataPaths.JobsDir, uid ?? "_", "runs.log");
        }

        public static string JobDir(string uid)
        {
            return Path.Combine(DataPaths.JobsDir, uid ?? "_");
        }

        private static void AppendRunLog(string uid, JobRunResult result)
        {
            string dir = Path.Combine(DataPaths.JobsDir, uid);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "runs.log");
            var sb = new StringBuilder();
            sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + (result.Ok ? "OK" : "FAIL") + " ====");
            if (result.Lines != null)
            {
                for (int i = 0; i < result.Lines.Count; i++)
                    sb.AppendLine(result.Lines[i] ?? "");
            }
            sb.AppendLine();
            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            TryTrimLog(path, 400 * 1024);
        }

        private static void TryTrimLog(string path, int maxBytes)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length <= maxBytes) return;
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (text.Length < maxBytes / 2) return;
                int keep = Math.Min(text.Length, maxBytes / 2);
                File.WriteAllText(path, text.Substring(text.Length - keep), Encoding.UTF8);
            }
            catch { }
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
            _jobs = new List<JobItem>();
            _writeBlocked = false;
            _loaded = true;
            _sampleSeeded = false;
            _sampleRev = 0;
            string path = DataPaths.JobsIndexJson;
            bool loadFailed = false;
            int fileFormat = 0;
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var file = JsonConvert.DeserializeObject<JobIndexFile>(json);
                    if (file != null)
                    {
                        fileFormat = file.FormatVersion;
                        _sampleSeeded = file.SampleSeeded;
                        _sampleRev = file.SampleRev;
                        if (file.Jobs != null)
                        {
                            for (int i = 0; i < file.Jobs.Count; i++)
                            {
                                var j = Normalize(file.Jobs[i], fileFormat);
                                if (j != null) _jobs.Add(j);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                loadFailed = true;
                _jobs = new List<JobItem>();
                _sampleSeeded = false;
                _sampleRev = 0;
                CompatDegrade.Once("jobflow-index-corrupt", "任务编排索引损坏，已尝试备份", ex);
            }
            if (loadFailed)
            {
                if (!DataFileGuard.TryQuarantine(path))
                    _writeBlocked = true;
                else
                {
                    _sampleSeeded = true;
                    _sampleRev = JobSamples.CurrentSampleRev;
                    try { WriteLocked(); }
                    catch { }
                }
                return;
            }
            if (!_sampleSeeded)
            {
                SeedSamplesLocked();
                _sampleSeeded = true;
                _sampleRev = JobSamples.CurrentSampleRev;
                try { WriteLocked(); }
                catch { }
            }
            else if (_sampleRev < JobSamples.CurrentSampleRev)
            {
                RefreshSamplesLocked();
                _sampleRev = JobSamples.CurrentSampleRev;
                try { WriteLocked(); }
                catch { }
            }
            else if (fileFormat < FormatVersion && !_writeBlocked)
            {
                // 旧版已迁移字段，回写 FormatVersion=2
                try { WriteLocked(); }
                catch { }
            }
        }

        private static void SeedSamplesLocked()
        {
            var samples = JobSamples.CreateAll();
            for (int s = 0; s < samples.Count; s++)
            {
                var stock = samples[s];
                bool exists = false;
                for (int i = 0; i < _jobs.Count; i++)
                {
                    if (_jobs[i].Uid == stock.Uid)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    _jobs.Insert(s, Clone(stock));
            }
            SeedRunnableIfMissingLocked();
        }

        private static void RefreshSamplesLocked()
        {
            var samples = JobSamples.CreateAll();
            for (int s = 0; s < samples.Count; s++)
            {
                var stock = samples[s];
                int idx = IndexOfLocked(stock.Uid);
                // 【示例】只读参考：版本升级时强制用出厂内容覆盖（保留运行统计）
                var next = Clone(stock);
                if (idx >= 0)
                {
                    var cur = _jobs[idx];
                    next.LastRunAt = cur.LastRunAt;
                    next.LastRunOk = cur.LastRunOk;
                    next.LastRunSummary = cur.LastRunSummary;
                    next.LastAutoAttemptAt = cur.LastAutoAttemptAt;
                    next.TestPassAt = cur.TestPassAt;
                    next.RunCount = cur.RunCount;
                    next.ManualSucceeded = cur.ManualSucceeded;
                    next.CreatedAt = cur.CreatedAt;
                    next.ModifiedAt = DateTime.Now;
                    _jobs[idx] = next;
                }
                else
                    _jobs.Insert(Math.Min(s, _jobs.Count), next);
            }
            SeedRunnableIfMissingLocked();
            PatchRunnableCaptionsLocked();
        }

        /// <summary>可运行示例只在缺失时插入，不覆盖用户已改内容。</summary>
        private static void SeedRunnableIfMissingLocked()
        {
            var extras = JobSamples.CreateRunnableSamples();
            for (int i = 0; i < extras.Count; i++)
            {
                var stock = extras[i];
                if (IndexOfLocked(stock.Uid) >= 0) continue;
                _jobs.Insert(0, Clone(stock));
            }
        }

        /// <summary>未改名的 DummyJSON 示例补上「需外网」标题；用户已改名则不动。</summary>
        private static void PatchRunnableCaptionsLocked()
        {
            var extras = JobSamples.CreateRunnableSamples();
            for (int i = 0; i < extras.Count; i++)
            {
                var stock = extras[i];
                int idx = IndexOfLocked(stock.Uid);
                if (idx < 0) continue;
                var cur = _jobs[idx];
                if (!JobSamples.NeedsRunnableCaptionRefresh(cur.Name)) continue;
                cur.Name = stock.Name;
                string remarks = cur.Remarks ?? "";
                if (remarks.IndexOf("外网", StringComparison.Ordinal) < 0)
                    cur.Remarks = "需访问外网 dummyjson.com（公开测试接口）。\n" + remarks;
            }
        }

        private static void WriteLocked()
        {
            Directory.CreateDirectory(DataPaths.JobsDir);
            var file = new JobIndexFile
            {
                FormatVersion = FormatVersion,
                SampleSeeded = _sampleSeeded,
                SampleRev = _sampleRev,
                Jobs = new List<JobItem>(_jobs.Count)
            };
            for (int i = 0; i < _jobs.Count; i++)
                file.Jobs.Add(Clone(_jobs[i]));
            string path = DataPaths.JobsIndexJson;
            string tmp = path + ".tmp";
            string json = JsonConvert.SerializeObject(file, Formatting.Indented);
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Copy(tmp, path, true);
            try { File.Delete(tmp); } catch { }
        }

        private static int IndexOfLocked(string uid)
        {
            for (int i = 0; i < _jobs.Count; i++)
            {
                if (_jobs[i].Uid == uid) return i;
            }
            return -1;
        }

        private static JobItem Normalize(JobItem j, int formatVersion)
        {
            if (j == null || string.IsNullOrEmpty(j.Uid)) return null;
            if (j.Params == null) j.Params = new List<JobParam>();
            if (j.Steps == null) j.Steps = new List<JobStep>();
            if (j.Trigger == null) j.Trigger = JobTrigger.Manual();
            if (j.Remarks == null) j.Remarks = "";
            if (j.RunCount < 0) j.RunCount = 0;
            if (formatVersion < 2)
                j.Status = j.Enabled ? JobStatus.Enabled : JobStatus.Disabled;
            j.SyncEnabledFromStatus();
            for (int i = 0; i < j.Steps.Count; i++)
            {
                if (j.Steps[i] == null) continue;
                if (j.Steps[i].Extracts == null) j.Steps[i].Extracts = new List<JobExtract>();
                if (j.Steps[i].TimeoutSec < 0) j.Steps[i].TimeoutSec = 0;
                if (string.IsNullOrEmpty(j.Steps[i].AllowedExitCodes)) j.Steps[i].AllowedExitCodes = "0";
                if (string.IsNullOrEmpty(j.Steps[i].LoopItemName)) j.Steps[i].LoopItemName = "item";
                if (j.Steps[i].LoopCount < 1) j.Steps[i].LoopCount = 1;
                if (j.Steps[i].ScriptBody == null) j.Steps[i].ScriptBody = "";
                if (j.Steps[i].PythonExe == null) j.Steps[i].PythonExe = "";
                JobHeaderHelper.Normalize(j.Steps[i]);
            }
            StripSecretDefaults(j);
            return j;
        }

        /// <summary>
        /// 密钥入参默认值不得进 index.json；本机记住走 DPAPI secrets.bin。
        /// 保存后会把内存副本的 DefaultValue 一并置空，避免界面或再次序列化带出明文（不是丢了用户输入）。
        /// </summary>
        public static void StripSecretDefaults(JobItem j)
        {
            if (j == null || j.Params == null) return;
            for (int i = 0; i < j.Params.Count; i++)
            {
                var p = j.Params[i];
                if (p == null) continue;
                if (p.Type == JobParamType.Secret)
                    p.DefaultValue = "";
            }
        }

        public static JobItem Clone(JobItem src)
        {
            if (src == null) return null;
            string json = JsonConvert.SerializeObject(src);
            var copy = JsonConvert.DeserializeObject<JobItem>(json);
            StripSecretDefaults(copy);
            return copy;
        }

        public static JobStep CloneStep(JobStep src)
        {
            if (src == null) return null;
            string json = JsonConvert.SerializeObject(src);
            return JsonConvert.DeserializeObject<JobStep>(json);
        }
    }

    /// <summary>计算自动触发是否到期（纯函数，便于单测）。</summary>
    public static class JobScheduleHelper
    {
        public static bool ShouldAutoRun(JobItem job, DateTime now)
        {
            if (!IsScheduleDue(job, now)) return false;
            return HasDefaultParams(job);
        }

        /// <summary>状态/触发/时刻/工作日等是否到期；不含密钥解密。</summary>
        public static bool IsScheduleDue(JobItem job, DateTime now)
        {
            if (job == null) return false;
            if (job.Status != JobStatus.Enabled) return false;
            if (job.Trigger == null || job.Trigger.Kind == JobTriggerKind.Manual) return false;
            if (!job.ManualSucceeded) return false;

            int interval = job.Trigger.IntervalMinutes;
            if (interval < 1) interval = 1;
            if (job.LastAutoAttemptAt.HasValue)
            {
                var since = now - job.LastAutoAttemptAt.Value;
                if (since.TotalMinutes < interval) return false;
            }

            if (job.Trigger.Kind == JobTriggerKind.Daily
                || job.Trigger.Kind == JobTriggerKind.Workday
                || job.Trigger.Kind == JobTriggerKind.Weekly)
            {
                TimeSpan at;
                if (!TryParseHHmm(job.Trigger.TimeHHmm, out at)) return false;
                var due = now.Date.Add(at);
                if (now < due) return false;
                if (job.LastAutoAttemptAt.HasValue && job.LastAutoAttemptAt.Value.Date == now.Date
                    && job.LastAutoAttemptAt.Value >= due)
                    return false;

                if (job.Trigger.Kind == JobTriggerKind.Workday)
                {
                    if (!WorkdayHelper.IsWorkday(now.Date, WorkcalStore.UserOff(), WorkcalStore.UserWork()))
                        return false;
                }
                if (job.Trigger.Kind == JobTriggerKind.Weekly)
                {
                    int want = job.Trigger.Weekday;
                    if (want < 0 || want > 6) want = 1;
                    if ((int)now.DayOfWeek != want) return false;
                }
                return true;
            }
            return false;
        }

        public static string FormatTriggerText(JobTrigger t)
        {
            if (t == null || t.Kind == JobTriggerKind.Manual) return "仅手动";
            string time = string.IsNullOrWhiteSpace(t.TimeHHmm) ? "09:00" : t.TimeHHmm.Trim();
            if (t.Kind == JobTriggerKind.Daily) return "每天 " + time;
            if (t.Kind == JobTriggerKind.Workday) return "工作日 " + time;
            if (t.Kind == JobTriggerKind.Weekly)
            {
                string[] names = { "日", "一", "二", "三", "四", "五", "六" };
                int wd = t.Weekday;
                if (wd < 0 || wd > 6) wd = 1;
                return "每周" + names[wd] + " " + time;
            }
            return "仅手动";
        }

        public static Dictionary<string, string> DefaultInputs(JobItem job)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (job == null || job.Params == null) return map;
            Dictionary<string, string> remembered = null;
            bool anySecret = false;
            for (int i = 0; i < job.Params.Count; i++)
            {
                if (job.Params[i] != null && job.Params[i].Type == JobParamType.Secret)
                {
                    anySecret = true;
                    break;
                }
            }
            string warn;
            if (anySecret && !string.IsNullOrEmpty(job.Uid))
                remembered = JobSecretStore.Load(job.Uid, out warn);
            for (int i = 0; i < job.Params.Count; i++)
            {
                var p = job.Params[i];
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                string name = p.Name.Trim();
                string val = "";
                if (p.Type == JobParamType.Secret)
                {
                    string secret;
                    if (remembered != null && remembered.TryGetValue(name, out secret) && !string.IsNullOrEmpty(secret))
                        val = secret;
                }
                else
                    val = p.DefaultValue ?? "";
                map[name] = val;
            }
            return map;
        }

        public static bool HasDefaultParams(JobItem job)
        {
            if (job == null || job.Params == null) return true;
            bool needSecret = false;
            for (int i = 0; i < job.Params.Count; i++)
            {
                var p = job.Params[i];
                if (p == null || !p.Required) continue;
                if (p.Type == JobParamType.Secret)
                {
                    needSecret = true;
                    continue;
                }
                if (string.IsNullOrEmpty(p.DefaultValue)) return false;
            }
            if (!needSecret) return true;
            if (string.IsNullOrEmpty(job.Uid)) return false;
            string warn;
            Dictionary<string, string> remembered = JobSecretStore.Load(job.Uid, out warn);
            for (int i = 0; i < job.Params.Count; i++)
            {
                var p = job.Params[i];
                if (p == null || !p.Required || p.Type != JobParamType.Secret) continue;
                string name = p.Name != null ? p.Name.Trim() : "";
                string secret;
                if (remembered == null || !remembered.TryGetValue(name, out secret) || string.IsNullOrEmpty(secret))
                    return false;
            }
            return true;
        }

        public static bool TryParseHHmm(string text, out TimeSpan span)
        {
            span = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;
            DateTime dt;
            if (DateTime.TryParseExact(text.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                span = dt.TimeOfDay;
                return true;
            }
            return false;
        }
    }
}
