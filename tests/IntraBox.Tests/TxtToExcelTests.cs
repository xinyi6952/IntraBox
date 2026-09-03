using System;
using System.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>文本转 Excel 解析纯逻辑测试（TxtToExcelHelper）。</summary>
    [TestClass]
    public class TxtToExcelTests
    {
        [TestMethod]
        public void DetectSeparator_逗号居多()
        {
            Assert.AreEqual(',', TxtToExcelHelper.DetectSeparator("a,b,c\n1,2,3"));
        }

        [TestMethod]
        public void DetectSeparator_制表符()
        {
            Assert.AreEqual('\t', TxtToExcelHelper.DetectSeparator("a\tb\n1\t2"));
        }

        [TestMethod]
        public void DetectSeparator_无分隔符_回退制表符()
        {
            Assert.AreEqual('\t', TxtToExcelHelper.DetectSeparator("abc"));
        }

        [TestMethod]
        public void Parse_逗号_行列与值正确()
        {
            var dt = TxtToExcelHelper.Parse("a,b\n1,2", ',');
            Assert.AreEqual(2, dt.Columns.Count);
            Assert.AreEqual(2, dt.Rows.Count);
            Assert.AreEqual("a", dt.Rows[0][0]);
            Assert.AreEqual("b", dt.Rows[0][1]);
            Assert.AreEqual("1", dt.Rows[1][0]);
            Assert.AreEqual("2", dt.Rows[1][1]);
        }

        [TestMethod]
        public void Parse_跳过空行()
        {
            var dt = TxtToExcelHelper.Parse("a,b\n\n1,2", ',');
            Assert.AreEqual(2, dt.Rows.Count);
        }

        [TestMethod]
        public void Parse_单元格去除首尾空白()
        {
            var dt = TxtToExcelHelper.Parse(" a , b ", ',');
            Assert.AreEqual("a", dt.Rows[0][0]);
            Assert.AreEqual("b", dt.Rows[0][1]);
        }
    }
}
