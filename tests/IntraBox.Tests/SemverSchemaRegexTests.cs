using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;
using Newtonsoft.Json.Linq;

namespace IntraBox.Tests
{
    /// <summary>Semver 版本比较 / JSON Schema / 正则解释 纯逻辑测试。</summary>
    [TestClass]
    public class SemverSchemaRegexTests
    {
        // ==================== Semver ====================

        [TestClass]
        public class Semver
        {
            [TestMethod]
            public void TryParse_标准三段_解析成功()
            {
                SemverUtil.Version v;
                string err;
                Assert.IsTrue(SemverUtil.TryParse("1.2.3", out v, out err));
                Assert.AreEqual(1, v.Major);
                Assert.AreEqual(2, v.Minor);
                Assert.AreEqual(3, v.Patch);
                Assert.IsNull(v.Pre);
            }

            [TestMethod]
            public void TryParse_v前缀与空白_可解析()
            {
                SemverUtil.Version v;
                string err;
                Assert.IsTrue(SemverUtil.TryParse(" v10.20.30 ", out v, out err));
                Assert.AreEqual(10, v.Major);
                Assert.AreEqual(20, v.Minor);
                Assert.AreEqual(30, v.Patch);
            }

            [TestMethod]
            public void TryParse_预发布后缀_解析出Pre()
            {
                SemverUtil.Version v;
                string err;
                Assert.IsTrue(SemverUtil.TryParse("1.0.0-rc.1", out v, out err));
                Assert.AreEqual("rc.1", v.Pre);
            }

            [TestMethod]
            public void TryParse_空或非法_返回false并给出错误()
            {
                SemverUtil.Version v;
                string err;
                Assert.IsFalse(SemverUtil.TryParse("", out v, out err));
                Assert.IsFalse(string.IsNullOrEmpty(err));
                Assert.IsFalse(SemverUtil.TryParse("不是版本", out v, out err));
                Assert.IsFalse(string.IsNullOrEmpty(err));
            }

            [TestMethod]
            public void Compare_主次补丁号_按位比较()
            {
                Assert.IsTrue(SemverUtil.Compare(Parse("1.2.3"), Parse("1.2.4")) < 0);
                Assert.IsTrue(SemverUtil.Compare(Parse("2.0.0"), Parse("1.9.9")) > 0);
                Assert.AreEqual(0, SemverUtil.Compare(Parse("1.2.3"), Parse("1.2.3")));
            }

            [TestMethod]
            public void Compare_预发布低于正式版()
            {
                Assert.IsTrue(SemverUtil.Compare(Parse("1.0.0-rc"), Parse("1.0.0")) < 0);
                Assert.IsTrue(SemverUtil.Compare(Parse("1.0.0"), Parse("1.0.0-rc")) > 0);
            }

            [TestMethod]
            public void CompareOp_运算符比较()
            {
                Assert.IsTrue(SemverUtil.CompareOp(Parse("1.2.4"), Parse("1.2.3"), ">"));
                Assert.IsTrue(SemverUtil.CompareOp(Parse("1.2.3"), Parse("1.2.4"), "<"));
                Assert.IsTrue(SemverUtil.CompareOp(Parse("1.2.3"), Parse("1.2.3"), "=="));
                Assert.IsTrue(SemverUtil.CompareOp(Parse("1.2.3"), Parse("1.2.3"), "="));
                Assert.IsFalse(SemverUtil.CompareOp(Parse("1.2.3"), Parse("1.2.3"), "!="));
            }

            [TestMethod]
            public void Satisfies_脱字符_上界独占()
            {
                string err;
                Assert.IsTrue(SemverUtil.Satisfies(Parse("1.2.3"), "^1.2.3", out err));
                Assert.IsTrue(SemverUtil.Satisfies(Parse("1.5.0"), "^1.2.3", out err));
                Assert.IsFalse(SemverUtil.Satisfies(Parse("2.0.0"), "^1.2.3", out err));
            }

            [TestMethod]
            public void Satisfies_波浪号_次版本上界()
            {
                string err;
                Assert.IsTrue(SemverUtil.Satisfies(Parse("1.2.9"), "~1.2.3", out err));
                Assert.IsFalse(SemverUtil.Satisfies(Parse("1.3.0"), "~1.2.3", out err));
            }

            [TestMethod]
            public void Satisfies_精确匹配()
            {
                string err;
                Assert.IsTrue(SemverUtil.Satisfies(Parse("1.2.3"), "1.2.3", out err));
                Assert.IsFalse(SemverUtil.Satisfies(Parse("1.2.4"), "1.2.3", out err));
            }

            private static SemverUtil.Version Parse(string s)
            {
                SemverUtil.Version v;
                string err;
                SemverUtil.TryParse(s, out v, out err);
                return v;
            }
        }

        // ==================== JSON Schema ====================

        [TestClass]
        public class JsonSchema
        {
            [TestMethod]
            public void BuildSchema_标量类型()
            {
                Assert.AreEqual("integer", (string)JsonSchemaUtil.BuildSchema(new JValue(1))["type"]);
                Assert.AreEqual("string", (string)JsonSchemaUtil.BuildSchema(new JValue("a"))["type"]);
                Assert.AreEqual("boolean", (string)JsonSchemaUtil.BuildSchema(new JValue(true))["type"]);
                Assert.AreEqual("number", (string)JsonSchemaUtil.BuildSchema(new JValue(1.5))["type"]);
            }

            [TestMethod]
            public void BuildSchema_对象_含properties和required()
            {
                var schema = JsonSchemaUtil.BuildSchema(JObject.Parse("{\"name\":\"a\",\"age\":1}"));
                Assert.AreEqual("object", (string)schema["type"]);
                Assert.IsNotNull(schema["properties"]);
                Assert.IsNotNull(schema["required"]);
                Assert.AreEqual("string", (string)schema["properties"]["name"]["type"]);
                Assert.AreEqual("integer", (string)schema["properties"]["age"]["type"]);
            }

            [TestMethod]
            public void BuildSchema_数组_含items()
            {
                var schema = JsonSchemaUtil.BuildSchema(JArray.Parse("[1,2,3]"));
                Assert.AreEqual("array", (string)schema["type"]);
                Assert.AreEqual("integer", (string)schema["items"]["type"]);
            }

            [TestMethod]
            public void BuildSchema_null_类型为null()
            {
                var schema = JsonSchemaUtil.BuildSchema(JValue.CreateNull());
                Assert.AreEqual("null", (string)schema["type"]);
            }

            [TestMethod]
            public void Validate_类型不匹配_报错()
            {
                var schema = JObject.Parse("{\"type\":\"string\"}");
                var errors = JsonSchemaUtil.Validate(new JValue(123), schema, "$");
                Assert.IsTrue(errors.Count > 0);
                Assert.IsTrue(errors[0].IndexOf("期望类型 string", StringComparison.Ordinal) >= 0);
            }

            [TestMethod]
            public void Validate_缺少必填字段_报错()
            {
                var schema = JObject.Parse("{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"integer\"}},\"required\":[\"a\"]}");
                var errors = JsonSchemaUtil.Validate(JObject.Parse("{}"), schema, "$");
                Assert.IsTrue(errors.Count > 0);
                Assert.IsTrue(errors[0].IndexOf("缺少必填字段 a", StringComparison.Ordinal) >= 0);
            }

            [TestMethod]
            public void Validate_合法数据_无错误()
            {
                var schema = JObject.Parse("{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"integer\"}},\"required\":[\"a\"]}");
                var errors = JsonSchemaUtil.Validate(JObject.Parse("{\"a\":1}"), schema, "$");
                Assert.AreEqual(0, errors.Count);
            }

            [TestMethod]
            public void Infer_对象_返回缩进JSON含type()
            {
                string s = JsonSchemaUtil.Infer(JObject.Parse("{\"a\":1}"));
                Assert.IsTrue(s.IndexOf("\"type\"", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(s.IndexOf("\"properties\"", StringComparison.Ordinal) >= 0);
            }

            [TestMethod]
            public void FormatErrors_空_返回校验通过()
            {
                Assert.AreEqual("校验通过", JsonSchemaUtil.FormatErrors(new List<string>()));
                Assert.AreEqual("校验通过", JsonSchemaUtil.FormatErrors(null));
            }
        }

        // ==================== 正则解释 ====================

        [TestClass]
        public class RegexExplain
        {
            [TestMethod]
            public void Explain_空或null_返回空()
            {
                Assert.AreEqual("", RegexExplainer.Explain(null));
                Assert.AreEqual("", RegexExplainer.Explain(""));
            }

            [TestMethod]
            public void Explain_数字与单词边界转义()
            {
                Assert.IsTrue(RegexExplainer.Explain(@"\d").IndexOf("数字", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(RegexExplainer.Explain(@"\b").IndexOf("单词边界", StringComparison.Ordinal) >= 0);
            }

            [TestMethod]
            public void Explain_锚点()
            {
                Assert.IsTrue(RegexExplainer.Explain("^").IndexOf("行首", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(RegexExplainer.Explain("$").IndexOf("行尾", StringComparison.Ordinal) >= 0);
            }

            [TestMethod]
            public void Explain_字符类()
            {
                Assert.IsTrue(RegexExplainer.Explain("[abc]").IndexOf("之一", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(RegexExplainer.Explain("[^abc]").IndexOf("非 abc", StringComparison.Ordinal) >= 0);
            }

            [TestMethod]
            public void Explain_量词()
            {
                Assert.IsTrue(RegexExplainer.Explain("a{2,3}").IndexOf("2~3 次", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(RegexExplainer.Explain("a{2,}").IndexOf("至少 2 次", StringComparison.Ordinal) >= 0);
            }

            [TestMethod]
            public void Explain_分组()
            {
                Assert.IsTrue(RegexExplainer.Explain("(?:ab)").IndexOf("非捕获", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(RegexExplainer.Explain("(?<n>ab)").IndexOf("命名", StringComparison.Ordinal) >= 0);
                Assert.IsTrue(RegexExplainer.Explain("(ab)").IndexOf("捕获分组", StringComparison.Ordinal) >= 0);
            }
        }
    }
}
