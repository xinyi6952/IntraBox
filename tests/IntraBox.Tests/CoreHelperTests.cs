using System;
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

        // ==================== 时间戳 ====================

        [TestClass]
        public class Timestamp
        {
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
                for (int i = 0; i < ModuleRegistry.All.Count; i++)
                {
                    string key = ModuleRegistry.All[i].Key;
                    Assert.AreNotEqual("ulidgen", key);
                    Assert.AreNotEqual("screengif", key);
                    if (key == "generator") hasGenerator = true;
                    if (key == "urlparse") hasUrlParse = true;
                    if (key == "codec") hasCodec = true;
                }
                Assert.IsTrue(hasGenerator);
                Assert.IsTrue(hasCodec);
                Assert.IsTrue(hasUrlParse, "URL 解析与 URL 编解码用途不同，应同时保留");
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
    }
}
