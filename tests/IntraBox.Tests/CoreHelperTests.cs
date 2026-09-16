using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>哈希 / Base64 / 时间戳 纯逻辑测试（Helper 已抽出）。</summary>
    [TestClass]
    public class CoreHelperTests
    {
        // ==================== 哈希 ====================

        [TestClass]
        public class Hash
        {
            [TestMethod]
            public void MD5_已知文本_返回RFC标准值()
            {
                var hash = HashHelper.ComputeHex("MD5", Encoding.UTF8.GetBytes("abc"));
                Assert.AreEqual("900150983cd24fb0d6963f7d28e17f72", hash);
            }

            [TestMethod]
            public void SHA256_已知文本_返回RFC标准值()
            {
                var hash = HashHelper.ComputeHex("SHA256", Encoding.UTF8.GetBytes("abc"));
                Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
            }

            [TestMethod]
            public void 同一算法_不同输入_结果不同()
            {
                var a = HashHelper.ComputeHex("SHA256", Encoding.UTF8.GetBytes("a"));
                var b = HashHelper.ComputeHex("SHA256", Encoding.UTF8.GetBytes("b"));
                Assert.AreNotEqual(a, b);
            }

            [TestMethod]
            public void ToHex_字节数组_返回小写十六进制()
            {
                Assert.AreEqual("0a0b0c", HashHelper.ToHex(new byte[] { 10, 11, 12 }));
            }
        }

        // ==================== Base64 ====================

        [TestClass]
        public class Base64
        {
            [TestMethod]
            public void 编码_中文_返回已知Base64()
            {
                Assert.AreEqual("5L2g5aW9", Base64Helper.Encode("你好"));
            }

            [TestMethod]
            public void 编码再解码_等于原值()
            {
                var original = "hello world 123";
                Assert.AreEqual(original, Base64Helper.Decode(Base64Helper.Encode(original)));
            }

            [TestMethod]
            [ExpectedException(typeof(FormatException))]
            public void 非法Base64_抛异常()
            {
                Base64Helper.Decode("不是合法的base64!!");
            }
        }

        // ==================== 时间转换 ====================

        [TestClass]
        public class Timestamp
        {
            private static readonly TimeSpan Cst = TimeSpan.FromHours(8);
            private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 9, 16, 15, 0, 0, Cst);

            [TestMethod]
            public void 时间戳0_等于1970起点()
            {
                var dto = TimestampHelper.FromUnixSeconds(0);
                Assert.AreEqual(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), dto);
            }

            [TestMethod]
            public void 时间转时间戳再转回_往返一致()
            {
                var dto = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
                var ts = TimestampHelper.ToUnixSeconds(dto);
                Assert.AreEqual(dto, TimestampHelper.FromUnixSeconds(ts));
            }

            [TestMethod]
            public void 秒和毫秒_相差1000倍()
            {
                var dto = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
                var sec = TimestampHelper.ToUnixSeconds(dto);
                var ms = TimestampHelper.ToUnixMilliseconds(dto);
                Assert.AreEqual(sec * 1000L, ms);
            }

            [TestMethod]
            public void Parse_Unix秒()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("0", Now, out r, out err), err);
                Assert.AreEqual("Unix 秒", r.Kind);
                Assert.AreEqual(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), r.Instant);
            }

            [TestMethod]
            public void Parse_Unix毫秒_13位()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("0".PadLeft(13, '0'), Now, out r, out err), err);
                Assert.AreEqual("Unix 毫秒", r.Kind);
            }

            [TestMethod]
            public void Parse_ISO带Z()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("2026-09-16T06:30:00Z", Now, out r, out err), err);
                Assert.AreEqual(6, r.Instant.UtcDateTime.Hour);
                Assert.AreEqual(30, r.Instant.UtcDateTime.Minute);
            }

            [TestMethod]
            public void Parse_RFC1123()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("Wed, 16 Sep 2026 06:30:00 GMT", Now, out r, out err), err);
                Assert.AreEqual(6, r.Instant.UtcDateTime.Hour);
                Assert.AreEqual(TimeSpan.Zero, r.Instant.Offset);
            }

            [TestMethod]
            public void Parse_RFC1123_星期写错仍按日期()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("Tue, 16 Sep 2026 06:30:00 GMT", Now, out r, out err), err);
                Assert.AreEqual(16, r.Instant.UtcDateTime.Day);
                Assert.AreEqual(6, r.Instant.UtcDateTime.Hour);
            }

            [TestMethod]
            public void Parse_中文日期()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("2026年9月16日 14:30:00", Now, out r, out err), err);
                Assert.AreEqual(2026, r.Instant.Year);
                Assert.AreEqual(9, r.Instant.Month);
                Assert.AreEqual(16, r.Instant.Day);
                Assert.AreEqual(14, r.Instant.Hour);
                Assert.AreEqual(Cst, r.Instant.Offset);
            }

            [TestMethod]
            public void Parse_英文月日()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("Sep 16, 2026 14:30:00", Now, out r, out err), err);
                Assert.AreEqual(9, r.Instant.Month);
                Assert.AreEqual(16, r.Instant.Day);
            }

            [TestMethod]
            public void Parse_年月缺日_当月1日()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("2026-09", Now, out r, out err), err);
                Assert.AreEqual(1, r.Instant.Day);
                Assert.AreEqual(0, r.Instant.Hour);
                Assert.AreEqual("年月", r.Kind);
            }

            [TestMethod]
            public void Parse_英文月年()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("Sep 2026", Now, out r, out err), err);
                Assert.AreEqual(2026, r.Instant.Year);
                Assert.AreEqual(9, r.Instant.Month);
                Assert.AreEqual(1, r.Instant.Day);
            }

            [TestMethod]
            public void Parse_ISO周_周一()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("2026-W38", Now, out r, out err), err);
                Assert.AreEqual(DayOfWeek.Monday, r.Instant.DayOfWeek);
                Assert.AreEqual(0, r.Instant.Hour);
            }

            [TestMethod]
            public void Parse_紧凑yyyyMMdd()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("20260916", Now, out r, out err), err);
                Assert.AreEqual(new DateTimeOffset(2026, 9, 16, 0, 0, 0, Cst), r.Instant);
            }

            [TestMethod]
            public void Parse_英文星期_含当天()
            {
                TimeParseResult r;
                string err;
                Assert.IsTrue(TimestampHelper.TryParse("Wednesday", Now, out r, out err), err);
                Assert.AreEqual(new DateTimeOffset(2026, 9, 16, 0, 0, 0, Cst), r.Instant);
                Assert.IsTrue(TimestampHelper.TryParse("Monday", Now, out r, out err), err);
                Assert.AreEqual(new DateTimeOffset(2026, 9, 21, 0, 0, 0, Cst), r.Instant);
            }

            [TestMethod]
            public void Parse_空_失败()
            {
                TimeParseResult r;
                string err;
                Assert.IsFalse(TimestampHelper.TryParse("  ", Now, out r, out err));
                Assert.AreEqual("请填写时间", err);
            }

            [TestMethod]
            public void FormatRows_含ISO与Unix()
            {
                var p = new TimeParseResult
                {
                    Instant = new DateTimeOffset(2026, 9, 16, 14, 30, 0, Cst),
                    Kind = "本地日期时间"
                };
                var rows = TimestampHelper.FormatRows(p, Cst);
                Assert.IsTrue(rows.Count >= 10);
                Assert.AreEqual("识别", rows[0].Name);
                Assert.IsTrue(Find(rows, "Unix 秒").Length > 0);
                Assert.IsTrue(Find(rows, "ISO UTC").EndsWith("Z", StringComparison.Ordinal));
                Assert.AreEqual("2026-09-16 14:30:00", Find(rows, "本地"));
                StringAssert.Contains(Find(rows, "中文"), "星期三");
            }

            private static string Find(List<TimeFormatRow> rows, string name)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Name == name) return rows[i].Value ?? "";
                }
                return "";
            }
        }

        // ==================== ULID / 模块去重 ====================

        [TestClass]
        public class UlidAndModules
        {
            [TestMethod]
            public void NewUlid_长度为26且字符集合法()
            {
                string id = UlidUtil.NewUlid();
                Assert.AreEqual(26, id.Length);
                const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
                for (int i = 0; i < id.Length; i++)
                    Assert.IsTrue(alphabet.IndexOf(id[i]) >= 0, "非法字符 " + id[i]);
            }

            [TestMethod]
            public void NewUlid_连续两次_结果不同()
            {
                Assert.AreNotEqual(UlidUtil.NewUlid(), UlidUtil.NewUlid());
            }

            [TestMethod]
            public void 模块注册_无独立ULID与GIF入口()
            {
                bool hasGenerator = false;
                bool hasUrlParse = false;
                bool hasCodec = false;
                bool hasWorkcal = false;
                bool hasDirsize = false;
                bool hasEnvvars = false;
                bool hasJobflow = false;
                bool hasEyecare = false;
                for (int i = 0; i < ModuleRegistry.All.Count; i++)
                {
                    string key = ModuleRegistry.All[i].Key;
                    Assert.AreNotEqual("ulidgen", key);
                    Assert.AreNotEqual("screengif", key);
                    Assert.AreNotEqual("diskusage", key);
                    if (key == "idgenerator") hasGenerator = true;
                    if (key == "urlparse") hasUrlParse = true;
                    if (key == "codec") hasCodec = true;
                    if (key == "workcal") hasWorkcal = true;
                    if (key == "dirsize") hasDirsize = true;
                    if (key == "envvars") hasEnvvars = true;
                    if (key == "jobflow") hasJobflow = true;
                    if (key == "eyecare") hasEyecare = true;
                }
                Assert.IsTrue(hasGenerator);
                Assert.IsTrue(hasCodec);
                Assert.IsTrue(hasUrlParse, "URL 解析与 URL 编解码用途不同，应同时保留");
                Assert.IsTrue(hasWorkcal, "工作日历应注册");
                Assert.IsTrue(hasDirsize, "目录大小应注册");
                Assert.IsTrue(hasEnvvars, "环境变量应注册");
                Assert.IsTrue(hasJobflow, "任务编排应注册");
                Assert.IsTrue(hasEyecare, "护眼模式应注册");
            }

            [TestMethod]
            public void 导航默认顺序_效率工具与剪贴板在最前()
            {
                Assert.AreEqual("效率工具", NavOrder.DefaultCategories[0]);
                Assert.AreEqual("系统工具", NavOrder.DefaultCategories[1]);
                Assert.AreEqual("clipboard", NavOrder.DefaultToolKeys[0]);
                Assert.AreEqual("launcher", NavOrder.DefaultToolKeys[1]);
                Assert.AreEqual("todo", NavOrder.DefaultToolKeys[2]);
                Assert.AreEqual("notes", NavOrder.DefaultToolKeys[3]);
                Assert.AreEqual("vault", NavOrder.DefaultToolKeys[4]);
                var list = ToolVisibility.ToggleableToolsDefault();
                Assert.IsTrue(list.Count > 5);
                Assert.AreEqual("clipboard", list[0].Key);
                Assert.AreEqual("launcher", list[1].Key);
                Assert.AreEqual("todo", list[2].Key);
                Assert.AreEqual("notes", list[3].Key);
                Assert.AreEqual("vault", list[4].Key);
                Assert.AreEqual("workcal", list[5].Key);
                Assert.AreEqual("machineinfo", list[6].Key);
                Assert.AreEqual("envvars", list[7].Key);
                Assert.AreEqual("jobflow", list[8].Key);
                int ka = System.Array.IndexOf(NavOrder.DefaultToolKeys, "keepawake");
                Assert.IsTrue(ka >= 0);
                Assert.AreEqual("eyecare", NavOrder.DefaultToolKeys[ka + 1]);
                Assert.AreEqual("settings", ToolVisibility.SettingsKey);
                int fs = System.Array.IndexOf(NavOrder.DefaultToolKeys, "filesearch");
                Assert.IsTrue(fs >= 0);
                Assert.AreEqual("dirsize", NavOrder.DefaultToolKeys[fs + 1]);
                Assert.IsTrue(list.Count > 3, "应含笔记模块");
            }
        }

        // ==================== 文本文件编码往返 ====================

        [TestClass]
        public class TextFile
        {
            [TestMethod]
            public void GBK中文_自动检测并按原编码写回字节一致()
            {
                string path = Path.Combine(Path.GetTempPath(), "intrabox-codec-gbk.txt");
                try
                {
                    var gbk = Encoding.GetEncoding(936);
                    var original = gbk.GetBytes("# 中文注释\r\n127.0.0.1 local.test\r\n");
                    File.WriteAllBytes(path, original);
                    EncodingChoice used;
                    bool uncertain;
                    string text = TextFileCodec.ReadAll(path, TextFileCodec.Auto, 1024 * 1024, out used, out uncertain);
                    Assert.IsTrue(text.IndexOf("中文注释", StringComparison.Ordinal) >= 0);
                    Assert.AreEqual(936, used.CodePage);
                    TextFileCodec.WriteAll(path, text, used);
                    CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
                }
                finally
                {
                    try { File.Delete(path); } catch { }
                }
            }

            [TestMethod]
            public void UTF8带BOM_自动检测并保留BOM()
            {
                string path = Path.Combine(Path.GetTempPath(), "intrabox-codec-utf8bom.txt");
                try
                {
                    var utf8 = new UTF8Encoding(false);
                    var body = utf8.GetBytes("你好 hosts\r\n");
                    var original = new byte[3 + body.Length];
                    original[0] = 0xEF;
                    original[1] = 0xBB;
                    original[2] = 0xBF;
                    Buffer.BlockCopy(body, 0, original, 3, body.Length);
                    File.WriteAllBytes(path, original);
                    EncodingChoice used;
                    bool uncertain;
                    string text = TextFileCodec.ReadAll(path, TextFileCodec.Auto, 1024 * 1024, out used, out uncertain);
                    Assert.IsTrue(text.IndexOf("你好", StringComparison.Ordinal) >= 0);
                    Assert.AreEqual(65001, used.CodePage);
                    Assert.IsTrue(used.EmitBom);
                    TextFileCodec.WriteAll(path, text, used);
                    CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
                }
                finally
                {
                    try { File.Delete(path); } catch { }
                }
            }

            [TestMethod]
            public void UTF8无BOM中文_检测为UTF8且不写BOM()
            {
                string path = Path.Combine(Path.GetTempPath(), "intrabox-codec-utf8.txt");
                try
                {
                    var original = new UTF8Encoding(false).GetBytes("# 测试注释\n");
                    File.WriteAllBytes(path, original);
                    EncodingChoice used;
                    bool uncertain;
                    string text = TextFileCodec.ReadAll(path, TextFileCodec.Auto, 1024 * 1024, out used, out uncertain);
                    Assert.IsTrue(text.IndexOf("测试注释", StringComparison.Ordinal) >= 0);
                    Assert.AreEqual(65001, used.CodePage);
                    Assert.IsFalse(used.EmitBom);
                    TextFileCodec.WriteAll(path, text, used);
                    CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
                }
                finally
                {
                    try { File.Delete(path); } catch { }
                }
            }
        }

        [TestClass]
        public class Memory
        {
            [TestMethod]
            public void FormatMb_字节换算()
            {
                Assert.AreEqual("0 MB", MemoryUsage.FormatMb(0));
                Assert.AreEqual("0 MB", MemoryUsage.FormatMb(-1));
                Assert.AreEqual("5 MB", MemoryUsage.FormatMb(5L * 1024 * 1024));
            }

            [TestMethod]
            public void Capture_工作集大于零()
            {
                var u = MemoryUsage.Capture();
                Assert.IsTrue(u.WorkingSetBytes > 0);
                Assert.IsTrue(u.WorkingSetText.EndsWith(" MB"));
            }
        }
    }
}
