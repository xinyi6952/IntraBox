using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class WorkdayHelperTests
    {
        private static readonly HashSet<string> Empty = new HashSet<string>();

        [TestMethod]
        public void IsWorkday_2026国庆_放假()
        {
            Assert.IsFalse(WorkdayHelper.IsWorkday(new DateTime(2026, 10, 1), Empty, Empty));
            Assert.IsFalse(WorkdayHelper.IsWorkday(new DateTime(2026, 10, 7), Empty, Empty));
        }

        [TestMethod]
        public void IsWorkday_2026国庆调休周六_上班()
        {
            Assert.IsTrue(WorkdayHelper.IsWorkday(new DateTime(2026, 10, 10), Empty, Empty));
            Assert.IsTrue(WorkdayHelper.IsWorkday(new DateTime(2026, 9, 20), Empty, Empty));
        }

        [TestMethod]
        public void IsWorkday_普通周末_休息()
        {
            Assert.IsFalse(WorkdayHelper.IsWorkday(new DateTime(2026, 9, 13), Empty, Empty));
        }

        [TestMethod]
        public void IsWorkday_用户覆盖优先于内置()
        {
            var off = new HashSet<string> { "2026-09-14" };
            var work = new HashSet<string> { "2026-10-01" };
            Assert.IsFalse(WorkdayHelper.IsWorkday(new DateTime(2026, 9, 14), off, work));
            Assert.IsTrue(WorkdayHelper.IsWorkday(new DateTime(2026, 10, 1), off, work));
        }

        [TestMethod]
        public void AddWorkdays_跨十一()
        {
            var d = WorkdayHelper.AddWorkdays(new DateTime(2026, 9, 30), 1, Empty, Empty);
            Assert.AreEqual(new DateTime(2026, 10, 8), d);
        }

        [TestMethod]
        public void IsWorkday_2025国庆调休周六_上班()
        {
            Assert.IsTrue(WorkdayHelper.IsWorkday(new DateTime(2025, 10, 11), Empty, Empty));
            Assert.IsFalse(WorkdayHelper.IsWorkday(new DateTime(2025, 10, 1), Empty, Empty));
        }

        [TestMethod]
        public void HasBuiltinYear_2026有_2027无()
        {
            Assert.IsTrue(WorkdayHelper.HasBuiltinYear(2026));
            Assert.IsTrue(WorkdayHelper.HasBuiltinYear(2025));
            Assert.IsFalse(WorkdayHelper.HasBuiltinYear(2027));
        }
    }

    [TestClass]
    public class WorkcalStoreTests
    {
        [TestMethod]
        public void TryParseYmd_只认yyyyMMdd()
        {
            string ymd;
            Assert.IsTrue(WorkcalStore.TryParseYmd("2026-09-14", out ymd));
            Assert.AreEqual("2026-09-14", ymd);
            Assert.IsTrue(WorkcalStore.TryParseYmd(" 2026-01-02 ", out ymd));
            Assert.AreEqual("2026-01-02", ymd);
            Assert.IsFalse(WorkcalStore.TryParseYmd("14/09/2026", out ymd));
            Assert.IsFalse(WorkcalStore.TryParseYmd("2026/9/14", out ymd));
            Assert.IsFalse(WorkcalStore.TryParseYmd("9-14-2026", out ymd));
            Assert.IsFalse(WorkcalStore.TryParseYmd("", out ymd));
            Assert.IsFalse(WorkcalStore.TryParseYmd(null, out ymd));
        }
    }

    [TestClass]
    public class CurlCommandHelperTests
    {
        [TestMethod]
        public void TryParse_bash复制_抽出方法头与体()
        {
            string text = "curl 'https://api.intra/orders' -X POST -H 'Authorization: Bearer t' --data-raw '{\"id\":1}'";
            CurlCommand cmd;
            string err;
            Assert.IsTrue(CurlCommandHelper.TryParse(text, out cmd, out err), err);
            Assert.AreEqual("POST", cmd.Method);
            Assert.AreEqual("https://api.intra/orders", cmd.Url);
            Assert.AreEqual(1, cmd.Headers.Count);
            Assert.AreEqual("Authorization", cmd.Headers[0].Key);
            Assert.AreEqual("Bearer t", cmd.Headers[0].Value);
            Assert.AreEqual("{\"id\":1}", cmd.Body);
        }

        [TestMethod]
        public void TryParse_仅URL_默认为GET()
        {
            CurlCommand cmd;
            string err;
            Assert.IsTrue(CurlCommandHelper.TryParse("curl https://oa.intra/health", out cmd, out err), err);
            Assert.AreEqual("GET", cmd.Method);
            Assert.AreEqual("https://oa.intra/health", cmd.Url);
        }

        [TestMethod]
        public void TryParse_data无X_当作POST()
        {
            CurlCommand cmd;
            string err;
            Assert.IsTrue(CurlCommandHelper.TryParse("curl https://oa.intra/x -d a=1", out cmd, out err), err);
            Assert.AreEqual("POST", cmd.Method);
            Assert.AreEqual("a=1", cmd.Body);
        }

        [TestMethod]
        public void TryParse_拒绝非http()
        {
            CurlCommand cmd;
            string err;
            Assert.IsFalse(CurlCommandHelper.TryParse("curl file:///tmp/x", out cmd, out err));
        }

        [TestMethod]
        public void TryParse_折行反斜杠()
        {
            string text = "curl https://oa.intra/a \\\n  -H 'Accept: json'";
            CurlCommand cmd;
            string err;
            Assert.IsTrue(CurlCommandHelper.TryParse(text, out cmd, out err), err);
            Assert.AreEqual("https://oa.intra/a", cmd.Url);
            Assert.AreEqual("Accept", cmd.Headers[0].Key);
        }

        [TestMethod]
        public void TryParse_cmd折行与insecure()
        {
            string text = "curl.exe \"https://oa.intra/a\" ^\r\n  -k -H \"Accept: json\"";
            CurlCommand cmd;
            string err;
            Assert.IsTrue(CurlCommandHelper.TryParse(text, out cmd, out err), err);
            Assert.IsTrue(cmd.Insecure);
            Assert.AreEqual("https://oa.intra/a", cmd.Url);
            Assert.AreEqual("Accept", cmd.Headers[0].Key);
        }
    }

    [TestClass]
    public class HttpProbeHelperTests
    {
        [TestMethod]
        public void TryParseUrl_https默认443()
        {
            HttpProbeTarget t;
            string err;
            Assert.IsTrue(HttpProbeHelper.TryParseUrl("https://oa.intra/health", out t, out err), err);
            Assert.AreEqual("oa.intra", t.Host);
            Assert.AreEqual(443, t.Port);
            Assert.IsTrue(t.UseSsl);
        }

        [TestMethod]
        public void TryParseUrl_显式端口()
        {
            HttpProbeTarget t;
            string err;
            Assert.IsTrue(HttpProbeHelper.TryParseUrl("http://127.0.0.1:8080/x", out t, out err), err);
            Assert.AreEqual("127.0.0.1", t.Host);
            Assert.AreEqual(8080, t.Port);
            Assert.IsFalse(t.UseSsl);
        }

        [TestMethod]
        public void TryParseUrl_拒绝非http()
        {
            HttpProbeTarget t;
            string err;
            Assert.IsFalse(HttpProbeHelper.TryParseUrl("file:///tmp/x", out t, out err));
        }
    }

    [TestClass]
    public class SearchTextHelperTests
    {
        [TestMethod]
        public void Score_包含城市名()
        {
            string name = "(UTC+08:00) 北京，重庆，香港特别行政区，乌鲁木齐";
            Assert.IsTrue(SearchTextHelper.Score("北京", name) >= 50);
            Assert.IsTrue(SearchTextHelper.Score("utc+08", name) >= 50);
        }

        [TestMethod]
        public void Score_系统Id()
        {
            Assert.IsTrue(SearchTextHelper.Score("china", "显示名", "China Standard Time") >= 50);
        }

        [TestMethod]
        public void Score_空查询_有文本即匹配()
        {
            Assert.AreEqual(1, SearchTextHelper.Score("", "东京"));
            Assert.AreEqual(0, SearchTextHelper.Score("zzz", "东京"));
        }
    }
}
