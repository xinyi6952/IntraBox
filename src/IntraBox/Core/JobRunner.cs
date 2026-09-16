using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace IntraBox.Core
{
    /// <summary>按步骤执行一条编排：入参 → REST/SOAP/程序/bat/Python/开链接 → 抽取；支持步骤循环与等待退出码。</summary>
    public static class JobRunner
    {
        public static JobRunResult Run(JobItem job, IDictionary<string, string> inputs, bool markManualSuccess, CancellationToken token)
        {
            var lines = new List<string>();
            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            var secrets = new List<string>();
            if (job == null)
                return Fail("编排为空", lines, vars);

            JobVarHelper.SeedSystemVars(vars, DateTime.Now);
            string err;
            if (!ApplyParams(job, inputs, vars, secrets, out err))
                return Fail(err, lines, vars);

            if (job.Steps != null)
            {
                for (int s = 0; s < job.Steps.Count; s++)
                    JobHeaderHelper.Normalize(job.Steps[s]);
            }

            lines.Add("开始：" + (job.Name ?? "") + " @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            bool overall = true;
            if (job.Steps != null)
            {
                for (int i = 0; i < job.Steps.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var step = job.Steps[i];
                    if (step == null || !step.Enabled) continue;
                    string title = string.IsNullOrEmpty(step.Name) ? ("步骤" + (i + 1)) : step.Name;
                    lines.Add("--- " + title + " (" + KindText(step.Kind) + ") ---");

                    bool stepOk;
                    try
                    {
                        stepOk = RunStepWithLoop(step, vars, secrets, lines, token, out err);
                    }
                    catch (OperationCanceledException)
                    {
                        lines.Add("已取消");
                        vars.Clear();
                        return new JobRunResult { Ok = false, Message = "已取消", Lines = lines, Vars = null };
                    }

                    if (!stepOk)
                    {
                        overall = false;
                        if (!string.IsNullOrEmpty(err))
                            lines.Add(JobVarHelper.RedactText(err, secrets));
                        if (!step.ContinueOnFail)
                        {
                            lines.Add("失败即停");
                            break;
                        }
                        lines.Add("失败继续");
                    }
                }
            }

            string summary = overall ? "成功" : "失败";
            lines.Add("结束：" + summary);
            if (markManualSuccess && overall)
                job.ManualSucceeded = true;
            // 变量字典仅运行期使用；返回前清空大字符串，避免调用方/日志链路长期持有
            vars.Clear();
            return new JobRunResult
            {
                Ok = overall,
                Message = summary,
                Lines = lines,
                Vars = null
            };
        }

        public static JobStepResult ProbeStep(JobStep step, IDictionary<string, string> vars, bool sendHttp, CancellationToken token)
        {
            if (step == null) return new JobStepResult { Ok = false, Message = "步骤为空" };
            var secrets = new List<string>();
            string urlTpl = step.Url;
            string url;
            string err;
            if (!JobVarHelper.TrySubstitute(urlTpl ?? "", vars ?? new Dictionary<string, string>(), out url, out err))
                return new JobStepResult { Ok = false, Message = err };
            var tcp = JobHttpHelper.ProbeTcp(url, 5000, token);
            if (!tcp.Ok || !sendHttp) return tcp;
            if (step.Kind == JobStepKind.Soap)
            {
                string env;
                if (!JobVarHelper.TrySubstitute(step.Envelope ?? "", vars, out env, out err))
                    return new JobStepResult { Ok = false, Message = err };
                return JobHttpHelper.SendSoap(url, env, step.SoapAction, step.SoapVersion, step.TimeoutSec, step.Insecure, token);
            }
            string body;
            if (!JobVarHelper.TrySubstitute(step.Body ?? "", vars, out body, out err))
                return new JobStepResult { Ok = false, Message = err };
            string headersText;
            if (!JobVarHelper.TrySubstitute(step.HeadersText ?? "", vars, out headersText, out err))
                return new JobStepResult { Ok = false, Message = err };
            return JobHttpHelper.SendRest(step.Method, url, JobVarHelper.ParseHeaders(headersText), body, step.ContentType, step.TimeoutSec, step.Insecure, token);
        }

        private static bool RunStepWithLoop(
            JobStep step,
            Dictionary<string, string> vars,
            List<string> secrets,
            List<string> lines,
            CancellationToken token,
            out string error)
        {
            error = null;
            var kind = (JobLoopHelper.LoopKind)step.LoopKind;
            if (kind == JobLoopHelper.LoopKind.None)
                return RunStepOnce(step, vars, secrets, lines, token, out error);

            if (kind == JobLoopHelper.LoopKind.Repeat)
            {
                int n;
                if (!JobLoopHelper.TryResolveRepeatCount(step.LoopCount, out n, out error))
                    return false;
                lines.Add("循环：重复 " + n + " 次（上限 " + JobLoopHelper.MaxIterations + "）");
                for (int i = 0; i < n; i++)
                {
                    token.ThrowIfCancellationRequested();
                    JobLoopHelper.ApplyItemVars(vars, step.LoopItemName, (i + 1).ToString(), i);
                    lines.Add("· 第 " + (i + 1) + "/" + n + " 次");
                    if (!RunStepOnce(step, vars, secrets, lines, token, out error))
                        return false;
                }
                return true;
            }

            if (kind == JobLoopHelper.LoopKind.Foreach)
            {
                string srcName = (step.LoopSource ?? "").Trim();
                if (string.IsNullOrEmpty(srcName))
                {
                    error = "遍历变量名未填写";
                    return false;
                }
                string srcVal;
                if (!vars.TryGetValue(srcName, out srcVal))
                {
                    error = "未定义循环源变量 " + srcName;
                    return false;
                }
                List<string> items;
                if (!JobLoopHelper.TryParseItems(srcVal, step.LoopItemProperty, out items, out error))
                    return false;
                lines.Add("循环：遍历 " + srcName + "，共 " + items.Count + " 项（上限 " + JobLoopHelper.MaxIterations + "）");
                for (int i = 0; i < items.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    JobLoopHelper.ApplyItemVars(vars, step.LoopItemName, items[i], i);
                    lines.Add("· 项 " + (i + 1) + "/" + items.Count + " = " + JobVarHelper.Truncate(JobVarHelper.RedactText(items[i], secrets), 80));
                    if (!RunStepOnce(step, vars, secrets, lines, token, out error))
                        return false;
                }
                return true;
            }

            error = "未知循环类型";
            return false;
        }

        private static bool RunStepOnce(
            JobStep step,
            Dictionary<string, string> vars,
            List<string> secrets,
            List<string> lines,
            CancellationToken token,
            out string error)
        {
            error = null;
            JobStepResult sr = RunStep(step, vars, secrets, token);
            string msg = JobVarHelper.RedactText(sr.Message ?? "", secrets);
            lines.Add(msg);
            if (!string.IsNullOrEmpty(sr.Body))
                lines.Add("摘要：" + JobVarHelper.Truncate(JobVarHelper.RedactText(sr.Body, secrets), 400));

            if (!sr.Ok)
            {
                error = sr.Message;
                sr.Body = null;
                return false;
            }

            if (!ApplyExtracts(step, sr.Body, vars, lines, out error))
            {
                sr.Body = null;
                return false;
            }
            sr.Body = null;
            return true;
        }

        private static bool ApplyParams(JobItem job, IDictionary<string, string> inputs, Dictionary<string, string> vars, List<string> secrets, out string error)
        {
            error = null;
            if (job.Params == null) return true;
            for (int i = 0; i < job.Params.Count; i++)
            {
                var p = job.Params[i];
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                string name = p.Name.Trim();
                if (!JobVarHelper.IsValidName(name))
                {
                    error = "入参名无效：" + name;
                    return false;
                }
                string val = null;
                if (inputs != null && inputs.ContainsKey(name))
                    val = inputs[name];
                else
                    val = p.DefaultValue;
                if (val == null) val = "";
                if (p.Required && string.IsNullOrEmpty(val))
                {
                    error = "缺省入参：" + name;
                    return false;
                }
                vars[name] = val;
                if (p.Type == JobParamType.Secret && !string.IsNullOrEmpty(val))
                    secrets.Add(val);
            }
            return true;
        }

        private static JobStepResult RunStep(JobStep step, Dictionary<string, string> vars, List<string> secrets, CancellationToken token)
        {
            if (step.Kind == JobStepKind.Rest) return RunRest(step, vars, token);
            if (step.Kind == JobStepKind.Soap) return RunSoap(step, vars, token);
            if (step.Kind == JobStepKind.OpenUrl) return RunOpenUrl(step, vars);
            if (step.Kind == JobStepKind.Program || step.Kind == JobStepKind.Bat) return RunProcess(step, vars, token);
            if (step.Kind == JobStepKind.Python) return RunPython(step, vars, token);
            return new JobStepResult { Ok = false, Message = "未知步骤类型" };
        }

        private static JobStepResult RunRest(JobStep step, Dictionary<string, string> vars, CancellationToken token)
        {
            string url, body, headersText, err;
            if (!JobVarHelper.TrySubstitute(step.Url ?? "", vars, out url, out err))
                return new JobStepResult { Ok = false, Message = err };
            if (!JobVarHelper.TrySubstitute(step.Body ?? "", vars, out body, out err))
                return new JobStepResult { Ok = false, Message = err };
            if (!JobVarHelper.TrySubstitute(step.HeadersText ?? "", vars, out headersText, out err))
                return new JobStepResult { Ok = false, Message = err };
            return JobHttpHelper.SendRest(step.Method, url, JobVarHelper.ParseHeaders(headersText), body, step.ContentType, step.TimeoutSec, step.Insecure, token);
        }

        private static JobStepResult RunSoap(JobStep step, Dictionary<string, string> vars, CancellationToken token)
        {
            string url, env, err;
            if (!JobVarHelper.TrySubstitute(step.Url ?? "", vars, out url, out err))
                return new JobStepResult { Ok = false, Message = err };
            if (!JobVarHelper.TrySubstitute(step.Envelope ?? "", vars, out env, out err))
                return new JobStepResult { Ok = false, Message = err };
            var r = JobHttpHelper.SendSoap(url, env, step.SoapAction, step.SoapVersion, step.TimeoutSec, step.Insecure, token);
            if (r.Ok && JobVarHelper.LooksLikeSoapFault(r.Body))
            {
                r.Ok = false;
                r.Message = (r.Message ?? "") + "；检测到 SOAP Fault";
            }
            return r;
        }

        private static JobStepResult RunOpenUrl(JobStep step, Dictionary<string, string> vars)
        {
            string url, err;
            if (!JobVarHelper.TrySubstitute(step.Url ?? "", vars, out url, out err))
                return new JobStepResult { Ok = false, Message = err };
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return new JobStepResult { Ok = false, Message = "只允许 http:// 或 https://" };
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return new JobStepResult { Ok = true, Message = "已打开浏览器：" + JobVarHelper.Truncate(url, 120) };
            }
            catch (Exception ex)
            {
                return new JobStepResult { Ok = false, Message = "打开失败：" + ex.RootMessage() };
            }
        }

        private static JobStepResult RunPython(JobStep step, Dictionary<string, string> vars, CancellationToken token)
        {
            string scriptBody, pathTpl, args, work, err;
            if (!JobVarHelper.TrySubstitute(step.ScriptBody ?? "", vars, out scriptBody, out err))
                return new JobStepResult { Ok = false, Message = err };
            if (!JobVarHelper.TrySubstitute(step.Path ?? "", vars, out pathTpl, out err))
                return new JobStepResult { Ok = false, Message = err };
            if (!JobVarHelper.TrySubstitute(step.Arguments ?? "", vars, out args, out err))
                return new JobStepResult { Ok = false, Message = err };
            if (!JobVarHelper.TrySubstitute(step.WorkDir ?? "", vars, out work, out err))
                return new JobStepResult { Ok = false, Message = err };

            string exe;
            string resolveErr;
            if (!JobPythonHelper.TryResolveExe(step.PythonExe, out exe, out resolveErr))
                return new JobStepResult { Ok = false, Message = resolveErr };

            string scriptPath = null;
            bool tempScript = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(scriptBody))
                {
                    scriptPath = Path.Combine(Path.GetTempPath(), "intrabox-job-" + Guid.NewGuid().ToString("N") + ".py");
                    File.WriteAllText(scriptPath, scriptBody, new System.Text.UTF8Encoding(false));
                    tempScript = true;
                }
                else
                {
                    scriptPath = (pathTpl ?? "").Trim();
                    if (scriptPath.Length == 0)
                        return new JobStepResult { Ok = false, Message = "Python 步骤请填写 .py 路径或内联脚本" };
                    if (!string.Equals(Path.GetExtension(scriptPath), ".py", StringComparison.OrdinalIgnoreCase))
                        return new JobStepResult { Ok = false, Message = "Python 脚本须为 .py 文件" };
                    if (!File.Exists(scriptPath))
                        return new JobStepResult { Ok = false, Message = "找不到 Python 脚本：" + scriptPath };
                }

                string key = JobVarHelper.StepExitCodeVar(step.Name);
                int timeoutMs = JobLimits.ToWaitTimeoutMs(step.TimeoutSec);
                var psi = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (JobPythonHelper.IsPyLauncher(exe))
                {
                    psi.FileName = exe;
                    psi.Arguments = "-3 \"" + scriptPath + "\"" + (string.IsNullOrEmpty(args) ? "" : (" " + args));
                }
                else
                {
                    psi.FileName = exe;
                    psi.Arguments = "\"" + scriptPath + "\"" + (string.IsNullOrEmpty(args) ? "" : (" " + args));
                }
                if (!string.IsNullOrWhiteSpace(work) && Directory.Exists(work))
                    psi.WorkingDirectory = work;

                using (var p = new Process { StartInfo = psi })
                {
                    if (!p.Start())
                        return new JobStepResult { Ok = false, Message = "启动 Python 失败" };
                    bool exited = WaitProcess(p, timeoutMs, token);
                    if (!exited)
                    {
                        try { if (!p.HasExited) p.Kill(); } catch { }
                        vars[key] = "-1";
                        return new JobStepResult
                        {
                            Ok = false,
                            Message = "等待 Python 超时（" + JobLimits.ClampTimeoutSec(step.TimeoutSec) + " 秒）已终止，变量 " + key + "=-1"
                        };
                    }
                    int code = p.ExitCode;
                    vars[key] = code.ToString();
                    bool ok = IsAllowedExitCode(code, step.AllowedExitCodes);
                    return new JobStepResult
                    {
                        Ok = ok,
                        Message = (ok ? "Python 结束" : "退出码不被允许")
                            + "：" + Path.GetFileName(scriptPath) + " " + key + "=" + code
                            + "（允许：" + FormatAllowed(step.AllowedExitCodes) + "）"
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new JobStepResult { Ok = false, Message = "Python 运行失败：" + ex.RootMessage() };
            }
            finally
            {
                if (tempScript && !string.IsNullOrEmpty(scriptPath))
                {
                    try { File.Delete(scriptPath); } catch { }
                }
            }
        }

        private static JobStepResult RunProcess(JobStep step, Dictionary<string, string> vars, CancellationToken token)
        {
            string path, args, work, err;
            if (!JobVarHelper.TrySubstitute(step.Path ?? "", vars, out path, out err))
                return new JobStepResult { Ok = false, Message = err };
            if (!JobVarHelper.TrySubstitute(step.Arguments ?? "", vars, out args, out err))
                return new JobStepResult { Ok = false, Message = err };
            if (!JobVarHelper.TrySubstitute(step.WorkDir ?? "", vars, out work, out err))
                return new JobStepResult { Ok = false, Message = err };
            path = (path ?? "").Trim();
            if (path.Length == 0)
                return new JobStepResult { Ok = false, Message = "未填写程序路径" };
            string ext = Path.GetExtension(path);
            if (step.Kind == JobStepKind.Bat)
            {
                if (!string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase))
                    return new JobStepResult { Ok = false, Message = "批处理步骤只允许 .bat" };
            }
            else
            {
                if (!string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase))
                    return new JobStepResult { Ok = false, Message = "程序步骤只允许 .exe / .lnk / .bat" };
            }
            if (string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".vbs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".ps1", StringComparison.OrdinalIgnoreCase))
                return new JobStepResult { Ok = false, Message = "不允许 .cmd / .vbs / .ps1" };
            if (!File.Exists(path))
                return new JobStepResult { Ok = false, Message = "找不到文件：" + path };

            string key = JobVarHelper.StepExitCodeVar(step.Name);
            // .lnk 用 Shell 启动后难以可靠等待子进程；启动即记成功、退出码占位 0
            bool waitExit = !string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase);

            try
            {
                var psi = new ProcessStartInfo();
                if (string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase))
                {
                    psi.FileName = GetCmdExe();
                    psi.Arguments = "/c \"" + path + "\"" + (string.IsNullOrEmpty(args) ? "" : (" " + args));
                    psi.UseShellExecute = false;
                }
                else if (waitExit)
                {
                    psi.FileName = path;
                    psi.Arguments = args ?? "";
                    psi.UseShellExecute = false;
                }
                else
                {
                    psi.FileName = path;
                    psi.Arguments = args ?? "";
                    psi.UseShellExecute = true;
                }
                if (!string.IsNullOrWhiteSpace(work) && Directory.Exists(work))
                    psi.WorkingDirectory = work;

                if (!waitExit)
                {
                    var launched = Process.Start(psi);
                    vars[key] = "0";
                    return new JobStepResult
                    {
                        Ok = true,
                        Message = "已启动快捷方式（不等待退出）：" + Path.GetFileName(path)
                            + (launched != null ? (" PID " + launched.Id) : "")
                    };
                }

                using (var p = new Process { StartInfo = psi })
                {
                    if (!p.Start())
                        return new JobStepResult { Ok = false, Message = "启动失败" };
                    int timeoutMs = JobLimits.ToWaitTimeoutMs(step.TimeoutSec);
                    bool exited = WaitProcess(p, timeoutMs, token);
                    if (!exited)
                    {
                        try { if (!p.HasExited) p.Kill(); } catch { }
                        vars[key] = "-1";
                        return new JobStepResult
                        {
                            Ok = false,
                            Message = "等待进程超时（" + JobLimits.ClampTimeoutSec(step.TimeoutSec) + " 秒）已终止，变量 " + key + "=-1"
                        };
                    }
                    int code = p.ExitCode;
                    vars[key] = code.ToString();
                    bool ok = IsAllowedExitCode(code, step.AllowedExitCodes);
                    return new JobStepResult
                    {
                        Ok = ok,
                        Message = (ok ? "进程结束" : "退出码不被允许")
                            + "：" + Path.GetFileName(path) + " " + key + "=" + code
                            + "（允许：" + FormatAllowed(step.AllowedExitCodes) + "）"
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new JobStepResult { Ok = false, Message = "启动/等待失败：" + ex.RootMessage() };
            }
        }

        private static bool WaitProcess(Process p, int timeoutMs, CancellationToken token)
        {
            int slice = 200;
            int waited = 0;
            while (timeoutMs <= 0 || waited < timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                if (p.WaitForExit(slice))
                    return true;
                waited += slice;
            }
            return p.HasExited;
        }

        public static bool IsAllowedExitCode(int code, string allowedText)
        {
            var set = ParseAllowedExitCodes(allowedText);
            return set.Contains(code);
        }

        public static HashSet<int> ParseAllowedExitCodes(string text)
        {
            var set = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(text))
            {
                set.Add(0);
                return set;
            }
            string[] parts = text.Replace(';', ',').Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                int v;
                if (int.TryParse(parts[i].Trim(), out v))
                    set.Add(v);
            }
            if (set.Count == 0) set.Add(0);
            return set;
        }

        private static string FormatAllowed(string text)
        {
            var set = ParseAllowedExitCodes(text);
            var list = new List<int>(set);
            list.Sort();
            return string.Join(",", list.ConvertAll(x => x.ToString()).ToArray());
        }

        private static string GetCmdExe()
        {
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string cmd = Path.Combine(sys, "cmd.exe");
            return File.Exists(cmd) ? cmd : "cmd.exe";
        }

        private static bool ApplyExtracts(JobStep step, string body, Dictionary<string, string> vars, List<string> lines, out string error)
        {
            error = null;
            if (step.Extracts == null || step.Extracts.Count == 0) return true;
            for (int i = 0; i < step.Extracts.Count; i++)
            {
                var ex = step.Extracts[i];
                if (ex == null || string.IsNullOrWhiteSpace(ex.Name)) continue;
                string name = ex.Name.Trim();
                if (!JobVarHelper.IsValidName(name))
                {
                    error = "抽取变量名无效：" + name;
                    return false;
                }
                string val;
                string err;
                bool ok;
                if (step.Kind == JobStepKind.Soap)
                    ok = JobVarHelper.TryExtractXPath(body, ex.Path, step.XmlNamespaces, ex.AllowEmpty, out val, out err);
                else
                    ok = JobVarHelper.TryExtractJson(body, ex.Path, ex.AllowEmpty, out val, out err);
                if (!ok)
                {
                    error = err;
                    return false;
                }
                bool capped;
                vars[name] = JobLimits.CapVarValue(val ?? "", out capped);
                if (capped)
                    lines.Add("抽取 " + name + " = " + JobVarHelper.Truncate(vars[name], 80) + "（已截断）");
                else
                    lines.Add("抽取 " + name + " = " + JobVarHelper.Truncate(val ?? "", 80));
            }
            return true;
        }

        private static string KindText(JobStepKind kind)
        {
            return JobStep.KindText(kind);
        }

        private static JobRunResult Fail(string msg, List<string> lines, Dictionary<string, string> vars)
        {
            lines.Add(msg);
            if (vars != null) vars.Clear();
            return new JobRunResult { Ok = false, Message = msg, Lines = lines, Vars = null };
        }
    }
}
