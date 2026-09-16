using System;
using System.Collections.Generic;

namespace IntraBox.Core
{
    public enum JobStepKind
    {
        Rest = 0,
        Soap = 1,
        Program = 2,
        Bat = 3,
        OpenUrl = 4,
        Python = 5
    }

    public enum JobParamType
    {
        Text = 0,
        Number = 1,
        Bool = 2,
        Secret = 3
    }

    public enum JobTriggerKind
    {
        Manual = 0,
        Daily = 1,
        Workday = 2,
        Weekly = 3
    }

    /// <summary>编排状态：草稿 / 启用 / 停用。自动跑仅 Status==Enabled。</summary>
    public enum JobStatus
    {
        Draft = 0,
        Enabled = 1,
        Disabled = 2
    }

    public sealed class JobParam
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public JobParamType Type { get; set; }
        public bool Required { get; set; }
        public string DefaultValue { get; set; }
        public string Hint { get; set; }
    }

    public sealed class JobExtract
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool AllowEmpty { get; set; }
    }

    public sealed class JobHeader
    {
        public bool Enabled { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }

        public static JobHeader Make(string name, string value, bool enabled)
        {
            return new JobHeader
            {
                Enabled = enabled,
                Name = name ?? "",
                Value = value ?? ""
            };
        }
    }

    public sealed class JobStep
    {
        public string Uid { get; set; }
        public string Name { get; set; }
        public JobStepKind Kind { get; set; }
        public bool Enabled { get; set; }
        public bool ContinueOnFail { get; set; }

        /// <summary>来自步骤库的 uid；编排里为快照，改库不影响已加入的任务。</summary>
        public string LibraryUid { get; set; }

        // REST
        public string Method { get; set; }
        public string Url { get; set; }
        /// <summary>Postman 式表；运行前同步到 HeadersText。</summary>
        public List<JobHeader> Headers { get; set; }
        public string HeadersText { get; set; }
        public string Body { get; set; }
        public string ContentType { get; set; }
        public int TimeoutSec { get; set; }
        public bool Insecure { get; set; }

        // SOAP
        public string SoapAction { get; set; }
        public string SoapVersion { get; set; }
        public string Envelope { get; set; }
        public string XmlNamespaces { get; set; }

        // Program / Bat / Python
        public string Path { get; set; }
        public string Arguments { get; set; }
        public string WorkDir { get; set; }

        /// <summary>允许的退出码，逗号/分号分隔；空则仅 0。</summary>
        public string AllowedExitCodes { get; set; }

        /// <summary>Python：内联脚本（非空则写入临时 .py 再跑；空则用 Path）。</summary>
        public string ScriptBody { get; set; }

        /// <summary>Python：解释器路径；空则解析 py -3 / python / python3。</summary>
        public string PythonExe { get; set; }

        /// <summary>0=无 1=重复N次 2=遍历变量（见 JobLoopHelper）。</summary>
        public int LoopKind { get; set; }
        public int LoopCount { get; set; }

        /// <summary>foreach 源变量名（其值为 JSON 数组或分号分隔列表）。</summary>
        public string LoopSource { get; set; }

        /// <summary>当前项变量名，默认 item。</summary>
        public string LoopItemName { get; set; }

        /// <summary>对象数组时取该属性作为 ${item}。</summary>
        public string LoopItemProperty { get; set; }

        public List<JobExtract> Extracts { get; set; }

        public static JobStep Create(JobStepKind kind)
        {
            var step = new JobStep
            {
                Uid = Guid.NewGuid().ToString("N"),
                Name = DefaultName(kind),
                Kind = kind,
                Enabled = true,
                Method = "GET",
                ContentType = "json",
                TimeoutSec = 0,
                SoapVersion = "1.1",
                AllowedExitCodes = "0",
                LoopKind = 0,
                LoopCount = 1,
                LoopItemName = "item",
                ScriptBody = "",
                Headers = new List<JobHeader>(),
                Extracts = new List<JobExtract>()
            };
            if (kind == JobStepKind.Rest)
            {
                step.Method = "POST";
                step.Headers = JobHeaderHelper.DefaultRestHeaders();
                JobHeaderHelper.SyncText(step);
            }
            return step;
        }

        public static string DefaultName(JobStepKind kind)
        {
            if (kind == JobStepKind.Rest) return "REST";
            if (kind == JobStepKind.Soap) return "SOAP";
            if (kind == JobStepKind.Program) return "程序";
            if (kind == JobStepKind.Bat) return "批处理";
            if (kind == JobStepKind.Python) return "Python";
            return "打开链接";
        }

        public static string KindText(JobStepKind kind)
        {
            if (kind == JobStepKind.Rest) return "REST";
            if (kind == JobStepKind.Soap) return "SOAP";
            if (kind == JobStepKind.Program) return "程序";
            if (kind == JobStepKind.Bat) return "批处理";
            if (kind == JobStepKind.Python) return "Python";
            if (kind == JobStepKind.OpenUrl) return "打开链接";
            return "未知";
        }
    }

    public sealed class JobTrigger
    {
        public JobTriggerKind Kind { get; set; }
        public string TimeHHmm { get; set; }
        public int IntervalMinutes { get; set; }
        public int Weekday { get; set; }

        public static JobTrigger Manual()
        {
            return new JobTrigger
            {
                Kind = JobTriggerKind.Manual,
                TimeHHmm = "09:00",
                IntervalMinutes = 60,
                Weekday = 1
            };
        }
    }

    public sealed class JobItem
    {
        public string Uid { get; set; }
        public string Name { get; set; }

        /// <summary>与 Status 同步：Status==Enabled 时为 true（JSON 兼容旧版）。</summary>
        public bool Enabled { get; set; }
        public JobStatus Status { get; set; }
        public string Remarks { get; set; }
        public List<JobParam> Params { get; set; }
        public List<JobStep> Steps { get; set; }
        public JobTrigger Trigger { get; set; }
        public bool ManualSucceeded { get; set; }
        public DateTime? LastRunAt { get; set; }
        public bool LastRunOk { get; set; }
        public string LastRunSummary { get; set; }
        public DateTime? LastAutoAttemptAt { get; set; }
        public DateTime? TestPassAt { get; set; }
        public int RunCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }

        public static JobItem Create(string name)
        {
            var now = DateTime.Now;
            return new JobItem
            {
                Uid = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(name) ? "新编排" : name.Trim(),
                Status = JobStatus.Draft,
                Enabled = false,
                Remarks = "",
                Params = new List<JobParam>(),
                Steps = new List<JobStep>(),
                Trigger = JobTrigger.Manual(),
                CreatedAt = now,
                ModifiedAt = now
            };
        }

        public static string StatusText(JobStatus s)
        {
            if (s == JobStatus.Enabled) return "启用";
            if (s == JobStatus.Disabled) return "停用";
            return "草稿";
        }

        /// <summary>Status 为真相源，回写 Enabled。</summary>
        public void SyncEnabledFromStatus()
        {
            Enabled = Status == JobStatus.Enabled;
        }
    }

    public sealed class JobIndexFile
    {
        public int FormatVersion { get; set; }
        public bool SampleSeeded { get; set; }
        public int SampleRev { get; set; }
        public List<JobItem> Jobs { get; set; }
    }

    public sealed class JobRunResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public List<string> Lines { get; set; }
        public Dictionary<string, string> Vars { get; set; }
    }

    public sealed class JobStepResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public int StatusCode { get; set; }
        public string Body { get; set; }
        public long ElapsedMs { get; set; }
    }

    public sealed class JobRunLogEntry
    {
        public DateTime Time { get; set; }
        public bool Ok { get; set; }
        public string Body { get; set; }

        public string TimeText
        {
            get { return Time.ToString("yyyy-MM-dd HH:mm:ss"); }
        }

        public string StatusText
        {
            get { return Ok ? "成功" : "失败"; }
        }
    }
}
