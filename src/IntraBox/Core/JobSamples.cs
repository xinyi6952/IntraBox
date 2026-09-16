using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace IntraBox.Core
{
    /// <summary>Factory sample jobs (placeholder URLs; Draft; teaching remarks).</summary>
    public static class JobSamples
    {
        public const int CurrentSampleRev = 5;

        public const string RestUid = "sample-job-rest-01";
        public const string SoapUid = "sample-job-soap-01";
        public const string ComplexUid = "sample-job-complex-01";
        public const string ProgramUid = "sample-job-program-01";
        /// <summary>可运行示例：DummyJSON 登录后用 token 查当前用户（不在 FixedUids，出现在编排列表）。</summary>
        public const string ChainUid = "sample-job-rest-chain-01";
        public const string ChainJobName = "【示例】DummyJSON 登录后查当前用户（需外网）";
        public const string ChainJobNameLegacy = "【示例】DummyJSON 登录后查当前用户";

        public const string LibRestUid = "sample-step-rest-01";
        public const string LibRestMeUid = "sample-step-rest-me-01";
        public const string LibSoapUid = "sample-step-soap-01";
        public const string LibPythonUid = "sample-step-python-01";
        public const string LibBatUid = "sample-step-bat-01";
        public const string LibUrlUid = "sample-step-openurl-01";

        public static readonly string[] FixedUids = { RestUid, SoapUid, ComplexUid, ProgramUid };
        public static readonly string[] FixedStepUids = { LibRestUid, LibRestMeUid, LibSoapUid, LibPythonUid, LibBatUid, LibUrlUid };

        public static List<JobItem> CreateAll()
        {
            return new List<JobItem>
            {
                CreateRestSample(),
                CreateSoapSample(),
                CreateComplexSample(),
                CreateProgramSample()
            };
        }

        /// <summary>出现在编排列表的可运行示例；已存在则不覆盖（用户可能已改）。</summary>
        public static List<JobItem> CreateRunnableSamples()
        {
            return new List<JobItem>
            {
                CreateRestChainSample()
            };
        }

        public static JobItem FindStock(string uid)
        {
            var all = CreateAll();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Uid == uid) return all[i];
            }
            return null;
        }

        public static string Fingerprint(JobItem job)
        {
            if (job == null) return "";
            var copy = JsonConvert.DeserializeObject<JobItem>(JsonConvert.SerializeObject(job));
            if (copy == null) return "";
            copy.LastRunAt = null;
            copy.LastRunOk = false;
            copy.LastRunSummary = null;
            copy.LastAutoAttemptAt = null;
            copy.TestPassAt = null;
            copy.RunCount = 0;
            copy.ManualSucceeded = false;
            copy.CreatedAt = default(DateTime);
            copy.ModifiedAt = default(DateTime);
            return JsonConvert.SerializeObject(copy);
        }

        /// <summary>出厂可运行示例仍用旧标题（未改名）时补「需外网」。</summary>
        public static bool NeedsRunnableCaptionRefresh(string currentName)
        {
            if (string.IsNullOrWhiteSpace(currentName)) return false;
            return string.Equals(currentName.Trim(), ChainJobNameLegacy, StringComparison.Ordinal);
        }

        public static bool IsFixedStepUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            for (int i = 0; i < FixedStepUids.Length; i++)
            {
                if (FixedStepUids[i] == uid) return true;
            }
            return false;
        }

        public static List<JobStep> CreateLibrarySteps()
        {
            var soapJob = CreateSoapSample();
            var complex = CreateComplexSample();
            var prog = CreateProgramSample();
            var list = new List<JobStep>();
            var login = CreateDummyLoginStep();
            login.Uid = LibRestUid;
            login.Name = "【示例】DummyJSON 登录取 token（需外网）";
            JobHeaderHelper.Normalize(login);
            list.Add(login);
            var me = CreateDummyMeStep();
            me.Uid = LibRestMeUid;
            me.Name = "【示例】DummyJSON 查当前用户（需外网）";
            JobHeaderHelper.Normalize(me);
            list.Add(me);
            if (soapJob.Steps != null && soapJob.Steps.Count > 0)
            {
                var s = soapJob.Steps[0];
                s.Uid = LibSoapUid;
                s.Name = "【示例】SOAP 查用户";
                list.Add(s);
            }
            if (complex.Steps != null && complex.Steps.Count > 2)
            {
                var s = complex.Steps[2];
                s.Uid = LibPythonUid;
                s.Name = "【示例】Python 汇总";
                list.Add(s);
            }
            if (prog.Steps != null && prog.Steps.Count > 0)
            {
                var s = prog.Steps[0];
                s.Uid = LibBatUid;
                s.Name = "【示例】预处理 bat";
                list.Add(s);
            }
            if (prog.Steps != null && prog.Steps.Count > 1)
            {
                var s = prog.Steps[1];
                s.Uid = LibUrlUid;
                s.Name = "【示例】打开结果页";
                list.Add(s);
            }
            return list;
        }

        public static JobStep FindStockStep(string uid)
        {
            var all = CreateLibrarySteps();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Uid == uid) return all[i];
            }
            return null;
        }

        public static bool IsFixedSampleUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            for (int i = 0; i < FixedUids.Length; i++)
            {
                if (FixedUids[i] == uid) return true;
            }
            return false;
        }

        private static JobItem CreateRestChainSample()
        {
            var job = Base(
                ChainJobName,
                ChainUid,
                "");
            job.Remarks =
                "需访问外网 dummyjson.com（公开测试接口，无需申请密钥）。\n"
                + "真实串联：步骤一登录 DummyJSON，用 JSONPath 取出 accessToken；"
                + "步骤二请求 /auth/me，Header 里带 Authorization: Bearer ${accessToken}。\n"
                + "账号为官网文档示例用户 emilys；密码填官网示例 emilyspass（密钥不进 index.json，可在立即运行时勾选本机记住）。\n"
                + "步骤超时默认留空表示不限制；离开本模块仍会取消后续请求。";
            job.Params.Add(new JobParam
            {
                Name = "username",
                DisplayName = "账号",
                Type = JobParamType.Text,
                Required = true,
                DefaultValue = "emilys"
            });
            job.Params.Add(new JobParam
            {
                Name = "password",
                DisplayName = "密码",
                Type = JobParamType.Secret,
                Required = true,
                DefaultValue = ""
            });
            var login = CreateDummyLoginStep();
            login.LibraryUid = LibRestUid;
            job.Steps.Add(login);
            var me = CreateDummyMeStep();
            me.LibraryUid = LibRestMeUid;
            job.Steps.Add(me);
            return job;
        }

        private static JobStep CreateDummyLoginStep()
        {
            var step = JobStep.Create(JobStepKind.Rest);
            step.Name = "登录取 token";
            step.Method = "POST";
            step.Url = "https://dummyjson.com/auth/login";
            step.ContentType = "json";
            step.TimeoutSec = 0;
            step.Body = "{\n  \"username\": \"${username}\",\n  \"password\": \"${password}\"\n}";
            JobHeaderHelper.Normalize(step);
            step.Extracts.Add(new JobExtract { Name = "accessToken", Path = "$.accessToken", AllowEmpty = false });
            return step;
        }

        private static JobStep CreateDummyMeStep()
        {
            var step = JobStep.Create(JobStepKind.Rest);
            step.Name = "查询当前用户";
            step.Method = "GET";
            step.Url = "https://dummyjson.com/auth/me";
            step.ContentType = "json";
            step.TimeoutSec = 0;
            step.Body = "";
            step.Headers = new List<JobHeader>
            {
                JobHeader.Make("Accept", "application/json", true),
                JobHeader.Make("Authorization", "Bearer ${accessToken}", true)
            };
            JobHeaderHelper.SyncText(step);
            step.Extracts.Add(new JobExtract { Name = "displayName", Path = "$.firstName", AllowEmpty = true });
            return step;
        }

        private static JobItem CreateRestSample()
        {
            var job = Base(
                "\u3010\u793a\u4f8b\u3011REST \u767b\u5f55\u53d6 token",
                RestUid,
                "");
            job.Remarks =
                "\u586b\u5199\u89c4\u8303\uff1a\n"
                + "1) URL/Header/Body \u53ef\u7528 ${\u53d8\u91cf}\uff0c\u7f3a\u53d8\u91cf\u5219\u8be5\u6b65\u5931\u8d25\u3002\n"
                + "2) \u5bc6\u94a5\u7c7b\u5165\u53c2\u9009\u300c\u5bc6\u94a5\u300d\uff0c\u8fd0\u884c\u65f6\u53ef\u52fe\u9009\u672c\u673a\u8bb0\u4f4f\uff08DPAPI\uff09\uff0c\u4e0d\u8fdb index.json\u3002\n"
                + "3) REST \u62bd\u53d6\u7528 JSONPath\uff0c\u5982\u4e0b\u4e00\u6b65\u8981\u7528 ${token}\u3002\n"
                + "4) \u53ef\u300c\u7c98\u8d34 curl\u300d\u586b\u5165\u672c\u6b65\uff08\u4e0d\u8c03\u7528 curl.exe\uff09\u3002\n"
                + "5) \u987b\u300c\u7acb\u5373\u8fd0\u884c\u300d\u6210\u529f\u4e00\u6b21\uff08ManualSucceeded\uff09\u540e\uff0c\u542f\u7528\u72b6\u6001\u624d\u5141\u8bb8\u81ea\u52a8\u8dd1\u3002\n"
                + "6) \u672c\u793a\u4f8b URL \u4e3a\u5360\u4f4d\uff0c\u8bf7\u6539\u6210\u5185\u7f51\u5730\u5740\u540e\u518d\u6d4b\u901a/\u8fd0\u884c\u3002";
            job.Params.Add(new JobParam
            {
                Name = "user",
                DisplayName = "\u8d26\u53f7",
                Type = JobParamType.Text,
                Required = true,
                DefaultValue = "demo"
            });
            job.Params.Add(new JobParam
            {
                Name = "password",
                DisplayName = "\u5bc6\u7801",
                Type = JobParamType.Secret,
                Required = true,
                DefaultValue = ""
            });
            var step = JobStep.Create(JobStepKind.Rest);
            step.Name = "\u767b\u5f55";
            step.Method = "POST";
            step.Url = "https://api.example.intra/login";
            step.ContentType = "json";
            step.HeadersText = "Content-Type: application/json\nAccept: application/json";
            step.Body = "{\n  \"user\": \"${user}\",\n  \"password\": \"${password}\"\n}";
            step.TimeoutSec = 60;
            step.Extracts.Add(new JobExtract { Name = "token", Path = "$.data.token", AllowEmpty = false });
            job.Steps.Add(step);
            return job;
        }

        private static JobItem CreateSoapSample()
        {
            var job = Base(
                "\u3010\u793a\u4f8b\u3011SOAP \u67e5\u7528\u6237",
                SoapUid,
                "");
            job.Remarks =
                "\u586b\u5199\u89c4\u8303\uff1a\n"
                + "1) SOAP \u53ef\u300c\u5bfc\u5165 WSDL\u300d\uff08URL \u6216\u672c\u5730 .wsdl\uff09\u9009\u64cd\u4f5c\u586b\u7aef\u70b9/Action/\u4fe1\u5c01 ${\u5b57\u6bb5}\u3002\n"
                + "2) \u590d\u6742\u591a\u7ed1\u5b9a/\u5de8\u578b schema \u4f1a\u5931\u8d25\u5e76\u4e2d\u6587\u63d0\u793a\uff0c\u6539\u624b\u5de5\u586b\u4fe1\u5c01\u3002\n"
                + "3) \u62bd\u53d6\u7528 XPath\uff0c\u53ef\u914d xmlns \u524d\u7f00\u8868\uff08\u6bcf\u884c prefix=uri\uff09\u3002\n"
                + "4) Fault \u89c6\u4e3a\u5931\u8d25\uff1b\u6d4b\u901a\u4ec5\u8dd1\u5f53\u524d\u6b65 TCP/HTTP\u3002\n"
                + "5) \u672c\u793a\u4f8b\u7aef\u70b9\u4e3a\u5360\u4f4d\uff0c\u8bf7\u66ff\u6362\u540e\u4fdd\u5b58\u3002";
            job.Params.Add(new JobParam
            {
                Name = "userId",
                DisplayName = "\u7528\u6237ID",
                Type = JobParamType.Text,
                Required = true,
                DefaultValue = "1001"
            });
            var step = JobStep.Create(JobStepKind.Soap);
            step.Name = "GetUser";
            step.Url = "http://localhost:8080/demo";
            step.SoapAction = "http://tempuri.org/GetUser";
            step.SoapVersion = "1.1";
            step.Envelope =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">\n"
                + "  <soap:Body>\n"
                + "    <GetUser xmlns=\"http://tempuri.org/\">\n"
                + "      <userId>${userId}</userId>\n"
                + "    </GetUser>\n"
                + "  </soap:Body>\n"
                + "</soap:Envelope>";
            step.XmlNamespaces = "soap=http://schemas.xmlsoap.org/soap/envelope/";
            step.TimeoutSec = 60;
            step.Extracts.Add(new JobExtract { Name = "userName", Path = "//name", AllowEmpty = true });
            job.Steps.Add(step);
            return job;
        }

        private static JobItem CreateComplexSample()
        {
            var job = Base(
                "\u3010\u793a\u4f8b\u3011\u767b\u5f55\u2192\u904d\u5386\u2192Python \u6c47\u603b",
                ComplexUid,
                "");
            job.Remarks =
                "\u586b\u5199\u89c4\u8303\uff1a\n"
                + "1) \u6b65\u9aa4\u987a\u5e8f\u6267\u884c\uff1b\u53ef\u5faa\u73af\u300c\u91cd\u590dN\u6b21/\u904d\u5386\u53d8\u91cf\u300d\uff08\u975e if/else\u3001\u975e\u5e76\u884c\u3001\u975e\u4efb\u610f\u811a\u672c\u5f15\u64ce\uff09\u3002\n"
                + "2) \u904d\u5386\u6e90\u4e3a JSON \u6570\u7ec4\u6216\u5206\u53f7\u5217\u8868\uff0c\u9879\u5199\u5165 ${item} / ${loop.index}\uff0c\u4e0a\u9650 50\u3002\n"
                + "3) Python \u662f\u6b65\u9aa4\u7c7b\u578b\uff1a\u586b .py \u8def\u5f84\u6216\u5185\u8054 ScriptBody\uff1b\u53ef\u9009 PythonExe\uff0c\u7a7a\u5219\u89e3\u6790 PATH\u3002\n"
                + "4) 程序/Python/bat 等待退出并写入退出码变量：步骤名为 login 这类 ASCII 合法标识时即 ${login_exitCode}；中文名则是 s + 步骤名 UTF-8 十六进制 + _exitCode，避免不同中文名撞成同一串下划线。允许退出码可逗号分隔。\n"
                + "5) \u89e6\u53d1\u5668\uff1a\u4ec5\u624b\u52a8/\u6bcf\u5929/\u5de5\u4f5c\u65e5/\u6bcf\u5468\uff1b\u81ea\u52a8\u8dd1\u9700\u542f\u7528 + \u624b\u52a8\u6210\u529f + \u5fc5\u586b\u6709\u9ed8\u8ba4/\u8bb0\u4f4f\u5bc6\u94a5\u3002\n"
                + "6) \u672c\u793a\u4f8b\u8def\u5f84\u4e0e URL \u5747\u4e3a\u5360\u4f4d\uff0c\u8bf7\u6309\u5185\u7f51\u89c4\u8303\u6539\u5199\u3002";
            job.Params.Add(new JobParam
            {
                Name = "baseUrl",
                DisplayName = "\u57fa\u5740",
                Type = JobParamType.Text,
                Required = true,
                DefaultValue = "https://api.example.intra"
            });
            job.Params.Add(new JobParam
            {
                Name = "ids",
                DisplayName = "\u8ba2\u5355\u5217\u8868",
                Type = JobParamType.Text,
                Required = true,
                DefaultValue = "A001;A002"
            });
            var login = JobStep.Create(JobStepKind.Rest);
            login.Name = "\u767b\u5f55";
            login.Method = "POST";
            login.Url = "${baseUrl}/auth/token";
            login.ContentType = "json";
            login.Body = "{\"client\":\"intrabox\"}";
            login.Extracts.Add(new JobExtract { Name = "token", Path = "$.token", AllowEmpty = false });
            job.Steps.Add(login);
            var call = JobStep.Create(JobStepKind.Rest);
            call.Name = "\u62c9\u53d6\u8ba2\u5355";
            call.Method = "GET";
            call.Url = "${baseUrl}/orders/${item}";
            call.HeadersText = "Authorization: Bearer ${token}";
            call.LoopKind = (int)JobLoopHelper.LoopKind.Foreach;
            call.LoopSource = "ids";
            call.LoopItemName = "item";
            job.Steps.Add(call);
            var py = JobStep.Create(JobStepKind.Python);
            py.Name = "汇总";
            py.Path = "";
            py.ScriptBody =
                "# 占位示例：本机需安装 Python；将上一步结果打印即可\n"
                + "import os\n"
                + "print('token=', os.environ.get('token', ''))\n"
                + "print('汇总完成')\n";
            py.Arguments = "";
            py.WorkDir = "";
            py.PythonExe = "";
            py.AllowedExitCodes = "0";
            py.TimeoutSec = 60;
            job.Steps.Add(py);
            return job;
        }

        private static JobItem CreateProgramSample()
        {
            var job = Base(
                "\u3010\u793a\u4f8b\u3011\u6279\u5904\u7406 + \u6253\u5f00\u94fe\u63a5",
                ProgramUid,
                "");
            job.Remarks =
                "\u586b\u5199\u89c4\u8303\uff1a\n"
                + "1) \u7a0b\u5e8f\u5141\u8bb8 .exe/.lnk/.bat\uff1b\u6279\u5904\u7406\u4ec5 .bat\uff1b\u62d2\u7edd .cmd/.vbs/.ps1\u3002\n"
                + "2) \u6253\u5f00\u94fe\u63a5\u4ec5 http(s)\uff1b\u4e0d\u7b49\u5f85\u9875\u9762\u3002\n"
                + "3) \u72b6\u6001\u9009\u300c\u8349\u7a3f\u300d\u65f6\u4e0d\u4f1a\u81ea\u52a8\u8dd1\uff1b\u6539\u300c\u542f\u7528\u300d\u4e14\u624b\u52a8\u6210\u529f\u540e\u624d\u5230\u70b9\u6267\u884c\u3002\n"
                + "4) \u672c\u793a\u4f8b\u8def\u5f84\u4e3a\u5360\u4f4d\uff0c\u8bf7\u6539\u6210\u771f\u5b9e bat \u4e0e\u5185\u7f51\u9875\u9762\u3002";
            job.Status = JobStatus.Draft;
            job.Enabled = false;
            var bat = JobStep.Create(JobStepKind.Bat);
            bat.Name = "预处理脚本";
            bat.Path = "C:\\tools\\prepare.bat";
            bat.Arguments = "/silent";
            bat.WorkDir = "C:\\tools";
            bat.AllowedExitCodes = "0,1";
            bat.TimeoutSec = 120;
            job.Steps.Add(bat);
            var url = JobStep.Create(JobStepKind.OpenUrl);
            url.Name = "打开结果页";
            url.Url = "https://oa.example.intra/report?from=jobflow";
            url.TimeoutSec = 30;
            job.Steps.Add(url);
            return job;
        }

        private static JobItem Base(string name, string uid, string remarks)
        {
            var now = DateTime.Now;
            return new JobItem
            {
                Uid = uid,
                Name = name,
                Status = JobStatus.Draft,
                Enabled = false,
                Remarks = remarks ?? "",
                Params = new List<JobParam>(),
                Steps = new List<JobStep>(),
                Trigger = JobTrigger.Manual(),
                CreatedAt = now,
                ModifiedAt = now,
                RunCount = 0
            };
        }
    }
}
