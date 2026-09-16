using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>REST Header 表与 HeadersText 互转；curl 填入；收集 ${变量}。</summary>
    public static class JobHeaderHelper
    {
        public static List<JobHeader> DefaultRestHeaders()
        {
            return new List<JobHeader>
            {
                JobHeader.Make("Content-Type", "application/json", true),
                JobHeader.Make("Accept", "application/json", true)
            };
        }

        public static void Normalize(JobStep step)
        {
            if (step == null) return;
            if (step.Headers == null) step.Headers = new List<JobHeader>();
            if (step.Headers.Count == 0 && !string.IsNullOrWhiteSpace(step.HeadersText))
                step.Headers = Parse(step.HeadersText);
            SyncText(step);
        }

        public static List<JobHeader> Parse(string text)
        {
            var list = new List<JobHeader>();
            var pairs = JobVarHelper.ParseHeaders(text);
            for (int i = 0; i < pairs.Count; i++)
                list.Add(JobHeader.Make(pairs[i].Key, pairs[i].Value, true));
            return list;
        }

        public static void SyncText(JobStep step)
        {
            if (step == null) return;
            if (step.Headers == null) step.Headers = new List<JobHeader>();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < step.Headers.Count; i++)
            {
                var h = step.Headers[i];
                if (h == null || !h.Enabled) continue;
                if (string.IsNullOrWhiteSpace(h.Name)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(h.Name.Trim());
                sb.Append(": ");
                sb.Append(h.Value ?? "");
            }
            step.HeadersText = sb.ToString();
        }

        public static bool ApplyCurl(JobStep step, CurlCommand cmd, out string error)
        {
            error = null;
            if (step == null || cmd == null)
            {
                error = "步骤或 curl 为空";
                return false;
            }
            step.Kind = JobStepKind.Rest;
            step.Method = string.IsNullOrEmpty(cmd.Method) ? "POST" : cmd.Method;
            step.Url = cmd.Url ?? "";
            step.Body = cmd.Body ?? "";
            step.Insecure = cmd.Insecure;
            step.ContentType = "json";
            var headers = new List<JobHeader>();
            bool hasCt = false;
            bool hasAccept = false;
            if (cmd.Headers != null)
            {
                for (int i = 0; i < cmd.Headers.Count; i++)
                {
                    string n = cmd.Headers[i].Key ?? "";
                    string v = cmd.Headers[i].Value ?? "";
                    headers.Add(JobHeader.Make(n, v, true));
                    if (n.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) hasCt = true;
                    if (n.Equals("Accept", StringComparison.OrdinalIgnoreCase)) hasAccept = true;
                }
            }
            if (!hasCt) headers.Insert(0, JobHeader.Make("Content-Type", "application/json", true));
            if (!hasAccept) headers.Add(JobHeader.Make("Accept", "application/json", true));
            step.Headers = headers;
            if (string.IsNullOrWhiteSpace(step.Name) || step.Name == JobStep.DefaultName(JobStepKind.Rest))
                step.Name = GuessName(step.Url);
            SyncText(step);
            return true;
        }

        public static bool TryApplyCurlText(JobStep step, string curlText, out string error)
        {
            CurlCommand cmd;
            if (!CurlCommandHelper.TryParse(curlText, out cmd, out error))
                return false;
            return ApplyCurl(step, cmd, out error);
        }

        public static string GuessName(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "REST";
            try
            {
                var u = new Uri(url);
                string path = (u.AbsolutePath ?? "").Trim('/');
                if (string.IsNullOrEmpty(path)) return u.Host;
                int slash = path.LastIndexOf('/');
                string last = slash >= 0 ? path.Substring(slash + 1) : path;
                return string.IsNullOrEmpty(last) ? "REST" : last;
            }
            catch
            {
                return "REST";
            }
        }

        public static List<string> CollectInputVars(JobItem job)
        {
            var names = new List<string>();
            if (job == null || job.Steps == null) return names;
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var s = job.Steps[i];
                if (s == null) continue;
                if (s.Extracts != null)
                {
                    for (int e = 0; e < s.Extracts.Count; e++)
                    {
                        if (s.Extracts[e] != null && !string.IsNullOrWhiteSpace(s.Extracts[e].Name))
                            outputs.Add(s.Extracts[e].Name.Trim());
                    }
                }
                string itemName = string.IsNullOrWhiteSpace(s.LoopItemName) ? "item" : s.LoopItemName.Trim();
                outputs.Add(itemName);
                outputs.Add(itemName + "_index");
                outputs.Add("loop.index");
                outputs.Add("loop.count");
                outputs.Add(JobVarHelper.StepExitCodeVar(s.Name));
            }
            for (int i = 0; i < job.Steps.Count; i++)
                CollectFromStep(job.Steps[i], names, outputs);
            return names;
        }

        public static void MergeParams(JobItem job)
        {
            if (job == null) return;
            if (job.Params == null) job.Params = new List<JobParam>();
            var want = CollectInputVars(job);
            var keep = new Dictionary<string, JobParam>(StringComparer.Ordinal);
            for (int i = 0; i < job.Params.Count; i++)
            {
                var p = job.Params[i];
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                keep[p.Name.Trim()] = p;
            }
            var next = new List<JobParam>();
            for (int i = 0; i < want.Count; i++)
            {
                string n = want[i];
                JobParam old;
                if (keep.TryGetValue(n, out old) && old != null)
                    next.Add(old);
                else
                {
                    next.Add(new JobParam
                    {
                        Name = n,
                        DisplayName = n,
                        Type = JobParamType.Text,
                        Required = true,
                        DefaultValue = ""
                    });
                }
            }
            job.Params = next;
        }

        public static JobStep SnapshotFromLibrary(JobStep lib)
        {
            var snap = JobFlowStore.CloneStep(lib);
            if (snap == null) return null;
            snap.LibraryUid = lib.Uid;
            snap.Uid = Guid.NewGuid().ToString("N");
            Normalize(snap);
            return snap;
        }

        public static string ValidateStep(JobStep step)
        {
            if (step == null) return "步骤为空";
            if (string.IsNullOrWhiteSpace(step.Name)) return "请填写步骤名称";
            if (step.Kind == JobStepKind.Rest || step.Kind == JobStepKind.Soap || step.Kind == JobStepKind.OpenUrl)
            {
                if (string.IsNullOrWhiteSpace(step.Url)) return "请填写 URL";
            }
            if (step.Kind == JobStepKind.Soap && string.IsNullOrWhiteSpace(step.Envelope))
                return "请填写 SOAP 信封";
            if (step.Kind == JobStepKind.Program || step.Kind == JobStepKind.Bat)
            {
                if (string.IsNullOrWhiteSpace(step.Path)) return "请填写路径";
            }
            if (step.Kind == JobStepKind.Python)
            {
                if (string.IsNullOrWhiteSpace(step.Path) && string.IsNullOrWhiteSpace(step.ScriptBody))
                    return "请填写 .py 路径或内联脚本（至少一项）";
            }
            return null;
        }

        private static void CollectFromStep(JobStep step, List<string> names, HashSet<string> skip)
        {
            if (step == null) return;
            JobVarHelper.CollectNames(step.Url, names, skip);
            JobVarHelper.CollectNames(step.Body, names, skip);
            JobVarHelper.CollectNames(step.Envelope, names, skip);
            JobVarHelper.CollectNames(step.Path, names, skip);
            JobVarHelper.CollectNames(step.Arguments, names, skip);
            JobVarHelper.CollectNames(step.ScriptBody, names, skip);
            JobVarHelper.CollectNames(step.SoapAction, names, skip);
            JobVarHelper.CollectNames(step.WorkDir, names, skip);
            JobVarHelper.CollectNames(step.LoopSource, names, skip);
            if (step.LoopKind == (int)JobLoopHelper.LoopKind.Foreach && !string.IsNullOrWhiteSpace(step.LoopSource))
            {
                string n = step.LoopSource.Trim();
                if ((skip == null || !skip.Contains(n)) && names.IndexOf(n) < 0)
                    names.Add(n);
            }
            JobVarHelper.CollectNames(step.HeadersText, names, skip);
            if (step.Headers != null)
            {
                for (int i = 0; i < step.Headers.Count; i++)
                {
                    if (step.Headers[i] == null) continue;
                    JobVarHelper.CollectNames(step.Headers[i].Value, names, skip);
                }
            }
        }
    }
}
