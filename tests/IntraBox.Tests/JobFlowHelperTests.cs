using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class JobVarHelperTests
    {
        [TestMethod]
        public void TrySubstitute_缺变量失败()
        {
            var vars = new Dictionary<string, string> { { "a", "1" } };
            string r, err;
            Assert.IsFalse(JobVarHelper.TrySubstitute("x=${b}", vars, out r, out err));
            StringAssert.Contains(err, "b");
            Assert.IsTrue(JobVarHelper.TrySubstitute("x=${a}", vars, out r, out err));
            Assert.AreEqual("x=1", r);
        }

        [TestMethod]
        public void TryExtractJson_路径()
        {
            string v, err;
            Assert.IsTrue(JobVarHelper.TryExtractJson("{\"data\":{\"token\":\"T1\"}}", "$.data.token", false, out v, out err), err);
            Assert.AreEqual("T1", v);
            Assert.IsFalse(JobVarHelper.TryExtractJson("{}", "$.missing", false, out v, out err));
            Assert.IsTrue(JobVarHelper.TryExtractJson("{}", "$.missing", true, out v, out err));
        }

        [TestMethod]
        public void TryExtractXPath_带命名空间()
        {
            string xml = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><r><ok>1</ok></r></s:Body></s:Envelope>";
            string v, err;
            Assert.IsTrue(JobVarHelper.TryExtractXPath(xml, "//ok", "", false, out v, out err), err);
            Assert.AreEqual("1", v);
            Assert.IsTrue(JobVarHelper.TryExtractXPath(xml, "//s:Body", "s=http://schemas.xmlsoap.org/soap/envelope/", true, out v, out err), err);
        }

        [TestMethod]
        public void Redact_敏感头与密钥值()
        {
            string s = JobVarHelper.RedactText("Authorization: Bearer SECRET\nX: 1", new List<string> { "SECRET" });
            StringAssert.Contains(s, "***");
            Assert.IsFalse(s.Contains("Bearer SECRET"));
        }

        [TestMethod]
        public void IsValidName()
        {
            Assert.IsTrue(JobVarHelper.IsValidName("token"));
            Assert.IsTrue(JobVarHelper.IsValidName("order_id"));
            Assert.IsFalse(JobVarHelper.IsValidName("1a"));
            Assert.IsFalse(JobVarHelper.IsValidName("a-b"));
        }

        [TestMethod]
        public void StepExitCodeVar_ASCII保持_中文不碰撞()
        {
            Assert.AreEqual("login_exitCode", JobVarHelper.StepExitCodeVar("login"));
            Assert.AreEqual("step_exitCode", JobVarHelper.StepExitCodeVar(""));
            string a = JobVarHelper.StepExitCodeVar("程序");
            string b = JobVarHelper.StepExitCodeVar("批处理");
            Assert.AreEqual(a, JobVarHelper.StepExitCodeVar("程序"));
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(JobVarHelper.IsValidName(a.Replace("_exitCode", "")));
            Assert.IsTrue(a.EndsWith("_exitCode", StringComparison.Ordinal));
            Assert.IsTrue(b.StartsWith("s", StringComparison.Ordinal));
            Assert.AreNotEqual("__exitCode", a);
            Assert.AreNotEqual("___exitCode", b);
        }
    }

    [TestClass]
    public class JobScheduleHelperTests
    {
        [TestMethod]
        public void ShouldAutoRun_需手动成功与默认入参()
        {
            var job = JobItem.Create("t");
            job.Status = JobStatus.Enabled;
            job.SyncEnabledFromStatus();
            job.ManualSucceeded = false;
            job.Trigger.Kind = JobTriggerKind.Daily;
            job.Trigger.TimeHHmm = "00:00";
            job.Trigger.IntervalMinutes = 1;
            Assert.IsFalse(JobScheduleHelper.ShouldAutoRun(job, DateTime.Now));

            job.ManualSucceeded = true;
            job.Params.Add(new JobParam { Name = "id", Required = true, DefaultValue = "" });
            Assert.IsFalse(JobScheduleHelper.HasDefaultParams(job));
            Assert.IsFalse(JobScheduleHelper.ShouldAutoRun(job, DateTime.Now));

            job.Params[0].DefaultValue = "1";
            Assert.IsTrue(JobScheduleHelper.HasDefaultParams(job));
            Assert.IsTrue(JobScheduleHelper.ShouldAutoRun(job, DateTime.Now));
        }

        [TestMethod]
        public void HasDefaultParams_密钥必填不能靠DefaultValue()
        {
            var job = JobItem.Create("t");
            job.Uid = "no-secrets-file";
            job.Params.Add(new JobParam
            {
                Name = "password",
                Type = JobParamType.Secret,
                Required = true,
                DefaultValue = "emilyspass"
            });
            Assert.IsFalse(JobScheduleHelper.HasDefaultParams(job));
            var map = JobScheduleHelper.DefaultInputs(job);
            Assert.AreEqual("", map["password"]);
        }

        [TestMethod]
        public void IsScheduleDue_未到点不进入HasDefaultParams()
        {
            var now = new DateTime(2026, 9, 15, 8, 0, 0);
            var job = JobItem.Create("t");
            job.Status = JobStatus.Enabled;
            job.SyncEnabledFromStatus();
            job.ManualSucceeded = true;
            job.Trigger.Kind = JobTriggerKind.Daily;
            job.Trigger.TimeHHmm = "09:00";
            job.Params.Add(new JobParam { Name = "password", Type = JobParamType.Secret, Required = true, DefaultValue = "x" });
            Assert.IsFalse(JobScheduleHelper.IsScheduleDue(job, now));
            Assert.IsFalse(JobScheduleHelper.ShouldAutoRun(job, now));
        }

        [TestMethod]
        public void ShouldAutoRun_草稿与停用不触发()
        {
            var job = JobItem.Create("t");
            job.ManualSucceeded = true;
            job.Trigger.Kind = JobTriggerKind.Daily;
            job.Trigger.TimeHHmm = "00:00";
            job.Trigger.IntervalMinutes = 1;
            job.Status = JobStatus.Draft;
            job.SyncEnabledFromStatus();
            Assert.IsFalse(JobScheduleHelper.ShouldAutoRun(job, DateTime.Now));
            job.Status = JobStatus.Disabled;
            job.SyncEnabledFromStatus();
            Assert.IsFalse(JobScheduleHelper.ShouldAutoRun(job, DateTime.Now));
            job.Status = JobStatus.Enabled;
            job.SyncEnabledFromStatus();
            Assert.IsTrue(JobScheduleHelper.ShouldAutoRun(job, DateTime.Now));
        }

        [TestMethod]
        public void ShouldAutoRun_同日已跑过不再触发()
        {
            var now = new DateTime(2026, 9, 14, 10, 0, 0);
            var job = JobItem.Create("t");
            job.Status = JobStatus.Enabled;
            job.SyncEnabledFromStatus();
            job.ManualSucceeded = true;
            job.Trigger.Kind = JobTriggerKind.Daily;
            job.Trigger.TimeHHmm = "09:00";
            job.Trigger.IntervalMinutes = 1;
            job.LastAutoAttemptAt = new DateTime(2026, 9, 14, 9, 5, 0);
            Assert.IsFalse(JobScheduleHelper.ShouldAutoRun(job, now));
        }

        [TestMethod]
        public void TryParseHHmm()
        {
            TimeSpan t;
            Assert.IsTrue(JobScheduleHelper.TryParseHHmm("9:30", out t));
            Assert.AreEqual(new TimeSpan(9, 30, 0), t);
            Assert.IsFalse(JobScheduleHelper.TryParseHHmm("xx", out t));
        }

        [TestMethod]
        public void Runner_缺变量步骤失败()
        {
            var job = JobItem.Create("x");
            var step = JobStep.Create(JobStepKind.OpenUrl);
            step.Url = "https://oa.intra/${orderId}";
            job.Steps.Add(step);
            var result = JobRunner.Run(job, new Dictionary<string, string>(), false, System.Threading.CancellationToken.None);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains(result.Message + string.Join("", result.Lines.ToArray()), "orderId");
        }
    }

    [TestClass]
    public class JobWsdlHelperTests
    {
        private const string SampleWsdl = @"<?xml version=""1.0"" encoding=""utf-8""?>
<definitions xmlns=""http://schemas.xmlsoap.org/wsdl/""
             xmlns:soap=""http://schemas.xmlsoap.org/wsdl/soap/""
             xmlns:tns=""http://tempuri.org/""
             xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
             targetNamespace=""http://tempuri.org/"">
  <types>
    <xsd:schema targetNamespace=""http://tempuri.org/"">
      <xsd:element name=""GetUser"">
        <xsd:complexType>
          <xsd:sequence>
            <xsd:element name=""userId"" type=""xsd:string""/>
            <xsd:element name=""token"" type=""xsd:string""/>
          </xsd:sequence>
        </xsd:complexType>
      </xsd:element>
      <xsd:element name=""GetUserResponse"">
        <xsd:complexType>
          <xsd:sequence>
            <xsd:element name=""name"" type=""xsd:string""/>
          </xsd:sequence>
        </xsd:complexType>
      </xsd:element>
    </xsd:schema>
  </types>
  <message name=""GetUserRequest"">
    <part name=""parameters"" element=""tns:GetUser""/>
  </message>
  <message name=""GetUserResponse"">
    <part name=""parameters"" element=""tns:GetUserResponse""/>
  </message>
  <portType name=""DemoPort"">
    <operation name=""GetUser"">
      <input message=""tns:GetUserRequest""/>
      <output message=""tns:GetUserResponse""/>
    </operation>
  </portType>
  <binding name=""DemoBinding"" type=""tns:DemoPort"">
    <soap:binding transport=""http://schemas.xmlsoap.org/soap/http"" style=""document""/>
    <operation name=""GetUser"">
      <soap:operation soapAction=""http://tempuri.org/GetUser"" style=""document""/>
      <input><soap:body use=""literal""/></input>
      <output><soap:body use=""literal""/></output>
    </operation>
  </binding>
  <service name=""DemoService"">
    <port name=""DemoPort"" binding=""tns:DemoBinding"">
      <soap:address location=""http://localhost:8080/demo""/>
    </port>
  </service>
</definitions>";

        [TestMethod]
        public void Parse_列出操作并生成信封占位()
        {
            var r = JobWsdlHelper.Parse(SampleWsdl);
            Assert.IsTrue(r.Ok, r.Error);
            Assert.AreEqual("http://localhost:8080/demo", r.Endpoint);
            Assert.AreEqual(1, r.Operations.Count);
            Assert.AreEqual("GetUser", r.Operations[0].Name);
            Assert.AreEqual("http://tempuri.org/GetUser", r.Operations[0].SoapAction);
            StringAssert.Contains(r.Operations[0].Envelope, "${userId}");
            StringAssert.Contains(r.Operations[0].Envelope, "${token}");
        }

        [TestMethod]
        public void Parse_非法XML失败()
        {
            var r = JobWsdlHelper.Parse("<not-wsdl>");
            Assert.IsFalse(r.Ok);
            Assert.IsFalse(string.IsNullOrEmpty(r.Error));
        }

        [TestMethod]
        public void Parse_内部DTD失败()
        {
            string xml = "<?xml version=\"1.0\"?><!DOCTYPE foo [<!ENTITY x \"x\">]><foo/>";
            var r = JobWsdlHelper.Parse(xml);
            Assert.IsFalse(r.Ok);
            Assert.IsFalse(string.IsNullOrEmpty(r.Error));
        }
    }

    [TestClass]
    public class JobLoopHelperTests
    {
        [TestMethod]
        public void TryParseItems_JSON字符串数组()
        {
            List<string> items;
            string err;
            Assert.IsTrue(JobLoopHelper.TryParseItems("[\"a\",\"b\"]", null, out items, out err), err);
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual("a", items[0]);
        }

        [TestMethod]
        public void TryParseItems_空数组与空元素失败()
        {
            List<string> items;
            string err;
            Assert.IsFalse(JobLoopHelper.TryParseItems("[]", null, out items, out err));
            StringAssert.Contains(err, "无有效项");
            Assert.IsFalse(JobLoopHelper.TryParseItems("[\"\",\"  \"]", null, out items, out err));
            StringAssert.Contains(err, "无有效项");
            Assert.IsFalse(JobLoopHelper.TryParseItems(";;\n", null, out items, out err));
            StringAssert.Contains(err, "无有效项");
        }

        [TestMethod]
        public void TryParseItems_对象属性()
        {
            List<string> items;
            string err;
            Assert.IsTrue(JobLoopHelper.TryParseItems("[{\"id\":\"1\"},{\"id\":\"2\"}]", "id", out items, out err), err);
            Assert.AreEqual("1", items[0]);
            Assert.AreEqual("2", items[1]);
        }

        [TestMethod]
        public void TryParseItems_分号列表与上限()
        {
            List<string> items;
            string err;
            Assert.IsTrue(JobLoopHelper.TryParseItems("x;y;z", null, out items, out err), err);
            Assert.AreEqual(3, items.Count);
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < JobLoopHelper.MaxIterations + 1; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(i).Append('"');
            }
            sb.Append(']');
            Assert.IsFalse(JobLoopHelper.TryParseItems(sb.ToString(), null, out items, out err));
            StringAssert.Contains(err, "上限");
        }

        [TestMethod]
        public void Runner_重复循环打开链接()
        {
            var job = JobItem.Create("loop");
            var step = JobStep.Create(JobStepKind.OpenUrl);
            step.Url = "https://example.com/${item}";
            step.LoopKind = (int)JobLoopHelper.LoopKind.Repeat;
            step.LoopCount = 2;
            step.LoopItemName = "item";
            job.Steps.Add(step);
            // OpenUrl 会真正 Process.Start；用缺变量验证循环替换次数更安全
            step.Url = "https://example.com/${missing}";
            var result = JobRunner.Run(job, new Dictionary<string, string>(), false, System.Threading.CancellationToken.None);
            Assert.IsFalse(result.Ok);
            StringAssert.Contains(string.Join("\n", result.Lines.ToArray()), "循环：重复");
        }

        [TestMethod]
        public void Runner_Foreach遍历()
        {
            var job = JobItem.Create("fe");
            job.Params.Add(new JobParam { Name = "ids", DefaultValue = "a;b", Type = JobParamType.Text });
            var step = JobStep.Create(JobStepKind.OpenUrl);
            step.Url = "https://example.com/${item}";
            step.LoopKind = (int)JobLoopHelper.LoopKind.Foreach;
            step.LoopSource = "ids";
            step.LoopItemName = "item";
            // 故意缺变量在第二层：用合法 URL 模板但我们只检查循环日志——OpenUrl 会启动浏览器
            // 改为缺变量以避免副作用
            step.Url = "https://example.com/${nope}";
            job.Steps.Add(step);
            var inputs = new Dictionary<string, string> { { "ids", "a;b" } };
            var result = JobRunner.Run(job, inputs, false, System.Threading.CancellationToken.None);
            Assert.IsFalse(result.Ok);
            string log = string.Join("\n", result.Lines.ToArray());
            StringAssert.Contains(log, "遍历");
            StringAssert.Contains(log, "项 1/");
        }
    }

    [TestClass]
    public class JobExitCodeHelperTests
    {
        [TestMethod]
        public void ParseAllowed_默认零()
        {
            var set = JobRunner.ParseAllowedExitCodes("");
            Assert.IsTrue(set.Contains(0));
            Assert.IsTrue(JobRunner.IsAllowedExitCode(0, null));
            Assert.IsFalse(JobRunner.IsAllowedExitCode(1, "0"));
            Assert.IsTrue(JobRunner.IsAllowedExitCode(1, "0,1"));
            Assert.IsTrue(JobRunner.IsAllowedExitCode(2, "0;2"));
        }
    }

    [TestClass]
    public class JobPythonHelperTests
    {
        [TestMethod]
        public void KindText_含Python()
        {
            Assert.AreEqual("Python", JobStep.KindText(JobStepKind.Python));
            Assert.AreEqual("Python", JobStep.DefaultName(JobStepKind.Python));
        }

        [TestMethod]
        public void TryResolveExe_指定不存在失败()
        {
            string exe, err;
            Assert.IsFalse(JobPythonHelper.TryResolveExe("C:\\definitely-missing-python-xyz.exe", out exe, out err));
            StringAssert.Contains(err, "找不到");
        }

        [TestMethod]
        public void IsPyLauncher()
        {
            Assert.IsTrue(JobPythonHelper.IsPyLauncher(@"C:\Windows\py.exe"));
            Assert.IsFalse(JobPythonHelper.IsPyLauncher(@"C:\Python\python.exe"));
        }
    }

    [TestClass]
    public class JobRunLogHelperTests
    {
        [TestMethod]
        public void Parse_分段倒序()
        {
            string text =
                "==== 2026-09-14 09:00:00 OK ====\n"
                + "line1\n"
                + "\n"
                + "==== 2026-09-14 10:00:00 FAIL ====\n"
                + "err\n";
            var list = JobRunLogHelper.Parse(text);
            Assert.AreEqual(2, list.Count);
            Assert.IsFalse(list[0].Ok);
            Assert.AreEqual(new DateTime(2026, 9, 14, 10, 0, 0), list[0].Time);
            StringAssert.Contains(list[0].Body, "err");
            Assert.IsTrue(list[1].Ok);
            StringAssert.Contains(list[1].Body, "line1");
        }

        [TestMethod]
        public void Parse_空文本()
        {
            Assert.AreEqual(0, JobRunLogHelper.Parse(null).Count);
            Assert.AreEqual(0, JobRunLogHelper.Parse("").Count);
        }
    }

    [TestClass]
    public class JobSamplesTests
    {
        [TestMethod]
        public void CreateAll_四条固定Uid草稿()
        {
            var all = JobSamples.CreateAll();
            Assert.AreEqual(4, all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                Assert.IsTrue(SampleGuard.HasExamplePrefix(all[i].Name));
                Assert.AreEqual(JobStatus.Draft, all[i].Status);
                Assert.IsTrue(JobSamples.IsFixedSampleUid(all[i].Uid));
                Assert.IsFalse(string.IsNullOrWhiteSpace(all[i].Remarks));
            }
        }

        [TestMethod]
        public void Fingerprint_忽略运行时字段()
        {
            var a = JobSamples.FindStock(JobSamples.RestUid);
            var b = JobFlowStore.Clone(a);
            b.RunCount = 9;
            b.LastRunAt = DateTime.Now;
            b.TestPassAt = DateTime.Now;
            b.ManualSucceeded = true;
            Assert.AreEqual(JobSamples.Fingerprint(a), JobSamples.Fingerprint(b));
        }

        [TestMethod]
        public void FormatTriggerText()
        {
            Assert.AreEqual("仅手动", JobScheduleHelper.FormatTriggerText(JobTrigger.Manual()));
            var d = JobTrigger.Manual();
            d.Kind = JobTriggerKind.Daily;
            d.TimeHHmm = "09:00";
            Assert.AreEqual("每天 09:00", JobScheduleHelper.FormatTriggerText(d));
        }

        [TestMethod]
        public void CreateRunnable_DummyJSON两步串联且不在只读列表()
        {
            var all = JobSamples.CreateRunnableSamples();
            Assert.AreEqual(1, all.Count);
            var job = all[0];
            Assert.AreEqual(JobSamples.ChainUid, job.Uid);
            Assert.IsFalse(JobSamples.IsFixedSampleUid(job.Uid));
            Assert.IsTrue(SampleGuard.HasExamplePrefix(job.Name));
            Assert.IsTrue(job.Name.IndexOf("需外网", StringComparison.Ordinal) >= 0);
            Assert.IsTrue((job.Remarks ?? "").IndexOf("外网", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(JobSamples.NeedsRunnableCaptionRefresh(JobSamples.ChainJobNameLegacy));
            Assert.IsFalse(JobSamples.NeedsRunnableCaptionRefresh(job.Name));
            Assert.AreEqual(2, job.Steps.Count);
            Assert.AreEqual(JobStepKind.Rest, job.Steps[0].Kind);
            Assert.IsTrue(job.Steps[0].Url.IndexOf("dummyjson.com/auth/login", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.AreEqual(0, job.Steps[0].TimeoutSec);
            Assert.AreEqual(0, job.Steps[1].TimeoutSec);
            Assert.AreEqual("accessToken", job.Steps[0].Extracts[0].Name);
            Assert.AreEqual("$.accessToken", job.Steps[0].Extracts[0].Path);
            Assert.AreEqual("GET", job.Steps[1].Method);
            Assert.IsTrue(job.Steps[1].Url.IndexOf("dummyjson.com/auth/me", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsTrue((job.Steps[1].HeadersText ?? "").IndexOf("${accessToken}", StringComparison.Ordinal) >= 0);
            Assert.IsNotNull(JobSamples.FindStockStep(JobSamples.LibRestMeUid));
            JobParam secret = null;
            for (int i = 0; i < job.Params.Count; i++)
            {
                if (job.Params[i].Type == JobParamType.Secret) secret = job.Params[i];
            }
            Assert.IsNotNull(secret);
            Assert.AreEqual("", secret.DefaultValue ?? "");
            var cloned = JobFlowStore.Clone(job);
            Assert.AreEqual("", cloned.Params[1].DefaultValue ?? "");
            var file = new JobIndexFile { Jobs = new List<JobItem> { cloned } };
            var round = Newtonsoft.Json.JsonConvert.DeserializeObject<JobIndexFile>(
                Newtonsoft.Json.JsonConvert.SerializeObject(file));
            JobParam secret2 = null;
            for (int i = 0; i < round.Jobs[0].Params.Count; i++)
            {
                if (round.Jobs[0].Params[i].Type == JobParamType.Secret)
                    secret2 = round.Jobs[0].Params[i];
            }
            Assert.IsNotNull(secret2);
            Assert.AreEqual("", secret2.DefaultValue ?? "");
        }
    }

    [TestClass]
    public class JobLimitsTests
    {
        [TestMethod]
        public void ClampTimeout_支持半小时与四小时上限()
        {
            Assert.AreEqual(JobLimits.DefaultTimeoutSec, JobLimits.ClampTimeoutSec(0));
            Assert.AreEqual(0, JobLimits.ClampTimeoutSec(-1));
            Assert.AreEqual(System.Threading.Timeout.Infinite, JobLimits.ToHttpTimeoutMs(0));
            Assert.AreEqual(0, JobLimits.ToWaitTimeoutMs(0));
            Assert.AreEqual(30000, JobLimits.ToWaitTimeoutMs(30));
            Assert.AreEqual(1800, JobLimits.ClampTimeoutSec(1800));
            Assert.AreEqual(JobLimits.MaxTimeoutSec, JobLimits.ClampTimeoutSec(JobLimits.MaxTimeoutSec));
            Assert.AreEqual(JobLimits.MaxTimeoutSec, JobLimits.ClampTimeoutSec(JobLimits.MaxTimeoutSec + 1));
        }

        [TestMethod]
        public void ReadUtf8Limited_截断超大流()
        {
            var bytes = new byte[100];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)'a';
            using (var ms = new System.IO.MemoryStream(bytes))
            {
                bool truncated;
                string text = JobLimits.ReadUtf8Limited(ms, 50, out truncated);
                Assert.IsTrue(truncated);
                Assert.IsTrue(text.Contains("响应已截断"));
                Assert.IsTrue(text.Length > 50);
            }
        }

        [TestMethod]
        public void CapVarValue_截断超长()
        {
            string big = new string('x', JobLimits.MaxVarChars + 10);
            bool truncated;
            string capped = JobLimits.CapVarValue(big, out truncated);
            Assert.IsTrue(truncated);
            Assert.IsTrue(capped.Length > JobLimits.MaxVarChars);
            Assert.IsTrue(capped.Contains("变量已截断"));
        }

        [TestMethod]
        public void Hints_非空()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(JobLimits.TimeoutHint()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(JobLimits.ResponseHint()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(JobLimits.MemoryHint()));
        }
    }

    [TestClass]
    public class JobHeaderHelperTests
    {
        [TestMethod]
        public void DefaultRest_含Json头()
        {
            var h = JobHeaderHelper.DefaultRestHeaders();
            Assert.AreEqual(2, h.Count);
            Assert.AreEqual("Content-Type", h[0].Name);
            Assert.AreEqual("application/json", h[0].Value);
            Assert.IsTrue(h[0].Enabled);
        }

        [TestMethod]
        public void ApplyCurl_bash填入并补默认Accept()
        {
            var step = JobStep.Create(JobStepKind.Rest);
            string err;
            Assert.IsTrue(JobHeaderHelper.TryApplyCurlText(step,
                "curl 'https://api.intra/login' -X POST -H 'Authorization: Bearer t' --data-raw '{\"a\":1}'",
                out err), err);
            Assert.AreEqual("POST", step.Method);
            Assert.AreEqual("https://api.intra/login", step.Url);
            Assert.AreEqual("{\"a\":1}", step.Body);
            Assert.IsTrue((step.HeadersText ?? "").IndexOf("Authorization", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsTrue((step.HeadersText ?? "").IndexOf("Accept", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsTrue((step.HeadersText ?? "").IndexOf("Content-Type", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [TestMethod]
        public void SyncText_未启用行不写入()
        {
            var step = JobStep.Create(JobStepKind.Rest);
            step.Headers = new List<JobHeader>
            {
                JobHeader.Make("Content-Type", "application/json", true),
                JobHeader.Make("X-Skip", "1", false)
            };
            JobHeaderHelper.SyncText(step);
            StringAssert.Contains(step.HeadersText, "Content-Type");
            Assert.IsFalse((step.HeadersText ?? "").Contains("X-Skip"));
        }

        [TestMethod]
        public void CollectInputVars_排除抽取与系统变量()
        {
            var job = JobItem.Create("t");
            var step = JobStep.Create(JobStepKind.Rest);
            step.Url = "https://x/${baseUrl}/a";
            step.Body = "{\"u\":\"${user}\",\"d\":\"${run.today}\"}";
            step.Extracts.Add(new JobExtract { Name = "token", Path = "$.t" });
            job.Steps.Add(step);
            JobHeaderHelper.MergeParams(job);
            var names = new List<string>();
            for (int i = 0; i < job.Params.Count; i++) names.Add(job.Params[i].Name);
            CollectionAssert.Contains(names, "baseUrl");
            CollectionAssert.Contains(names, "user");
            CollectionAssert.DoesNotContain(names, "run.today");
            CollectionAssert.DoesNotContain(names, "token");
        }

        [TestMethod]
        public void CollectInputVars_跳过循环系统变量()
        {
            var job = JobItem.Create("t");
            var step = JobStep.Create(JobStepKind.Rest);
            step.LoopKind = (int)JobLoopHelper.LoopKind.Foreach;
            step.LoopSource = "ids";
            step.LoopItemName = "item";
            step.Url = "https://x/${ids}/${item}/${item_index}/${loop.index}/${loop.count}";
            job.Steps.Add(step);
            var names = JobHeaderHelper.CollectInputVars(job);
            CollectionAssert.Contains(names, "ids");
            CollectionAssert.DoesNotContain(names, "item");
            CollectionAssert.DoesNotContain(names, "item_index");
            CollectionAssert.DoesNotContain(names, "loop.index");
            CollectionAssert.DoesNotContain(names, "loop.count");
        }

        [TestMethod]
        public void CollectInputVars_退出码与Runner一致()
        {
            var job = JobItem.Create("t");
            var step = JobStep.Create(JobStepKind.Bat);
            step.Name = "批处理";
            step.Arguments = "${" + JobVarHelper.StepExitCodeVar("批处理") + "}";
            job.Steps.Add(step);
            var names = JobHeaderHelper.CollectInputVars(job);
            CollectionAssert.DoesNotContain(names, JobVarHelper.StepExitCodeVar("批处理"));
        }

        [TestMethod]
        public void StripSecretDefaults_序列化不含密钥明文()
        {
            var job = JobItem.Create("t");
            job.Params.Add(new JobParam
            {
                Name = "password",
                Type = JobParamType.Secret,
                Required = true,
                DefaultValue = "emilyspass"
            });
            string raw = Newtonsoft.Json.JsonConvert.SerializeObject(job);
            Assert.IsTrue(raw.IndexOf("emilyspass", StringComparison.Ordinal) >= 0);
            var copy = JobFlowStore.Clone(job);
            Assert.AreEqual("", copy.Params[0].DefaultValue);
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(copy);
            Assert.IsFalse(json.IndexOf("emilyspass", StringComparison.Ordinal) >= 0);
            var file = new JobIndexFile { Jobs = new List<JobItem> { copy } };
            string indexJson = Newtonsoft.Json.JsonConvert.SerializeObject(file, Newtonsoft.Json.Formatting.Indented);
            Assert.IsFalse(indexJson.IndexOf("emilyspass", StringComparison.Ordinal) >= 0);
            StringAssert.Contains(indexJson, "\"DefaultValue\": \"\"");
        }

        [TestMethod]
        public void MaxSteps_溢出判定()
        {
            var job = JobItem.Create("t");
            for (int i = 0; i < 6; i++)
                job.Steps.Add(JobStep.Create(JobStepKind.OpenUrl));
            Assert.IsTrue(JobLimits.IsStepOverflow(job));
            job.Steps.RemoveAt(5);
            Assert.IsFalse(JobLimits.IsStepOverflow(job));
        }

        [TestMethod]
        public void ValidateStep_Python须路径或脚本()
        {
            var step = JobStep.Create(JobStepKind.Python);
            step.Name = "py";
            step.Path = "";
            step.ScriptBody = "";
            Assert.IsNotNull(JobHeaderHelper.ValidateStep(step));
            step.ScriptBody = "print(1)";
            Assert.IsNull(JobHeaderHelper.ValidateStep(step));
        }

        [TestMethod]
        public void Snapshot_换新Uid保留库编号()
        {
            var lib = JobStep.Create(JobStepKind.Rest);
            lib.Uid = "lib-1";
            lib.Name = "登录";
            var snap = JobHeaderHelper.SnapshotFromLibrary(lib);
            Assert.AreEqual("lib-1", snap.LibraryUid);
            Assert.AreNotEqual(lib.Uid, snap.Uid);
            Assert.AreEqual("登录", snap.Name);
        }
    }
}
