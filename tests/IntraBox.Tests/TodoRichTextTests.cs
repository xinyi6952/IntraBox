using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Modules.Todo;

namespace IntraBox.Tests
{
    [TestClass]
    public class TodoRichTextTests
    {
        [TestMethod]
        public void HasFieldTemplate_新标签顺序正确_通过()
        {
            string plain = "【任务标题】: 联调说明\n【任务来源】: 工单\n【任务描述】: 周五前提交\n";
            Assert.IsTrue(TodoRichText.HasFieldTemplate(plain));
            Assert.AreEqual("联调说明", TodoRichText.ExtractTitle(plain));
            Assert.AreEqual("工单", TodoRichText.ExtractField(plain, "任务来源"));
            Assert.AreEqual("周五前提交", TodoRichText.ExtractField(plain, "任务描述"));
        }

        [TestMethod]
        public void HasFieldTemplate_旧标签仍可通过()
        {
            string plain = "任务标题: 旧标题\r\n任务来源: 旧来源\r\n任务描述:\r\n第一行\r\n第二行\r\n";
            Assert.IsTrue(TodoRichText.HasFieldTemplate(plain));
            Assert.AreEqual("旧标题", TodoRichText.ExtractTitle(plain));
            Assert.AreEqual("旧来源", TodoRichText.ExtractField(plain, "任务来源"));
            Assert.AreEqual("第一行\n第二行", TodoRichText.ExtractField(plain, "任务描述").Replace("\r\n", "\n"));
        }

        [TestMethod]
        public void HasFieldTemplate_缺标签或乱序_不通过()
        {
            Assert.IsFalse(TodoRichText.HasFieldTemplate(""));
            Assert.IsFalse(TodoRichText.HasFieldTemplate("【任务标题】:\n【任务描述】:\n"));
            Assert.IsFalse(TodoRichText.HasFieldTemplate("【任务来源】:\n【任务标题】:\n【任务描述】:\n"));
            Assert.IsFalse(TodoRichText.HasFieldTemplate("备注\n【任务标题】:\n【任务来源】:\n【任务描述】:\n"));
            Assert.IsFalse(TodoRichText.HasFieldTemplate("【任务标题】:\n任务来源:\n【任务描述】:\n"));
        }

        [TestMethod]
        public void ExtractTitle_示例标题带书名号()
        {
            string plain = "【任务标题】: 【示例】周五 18:00 前提交接口联调说明\n【任务来源】:\n【任务描述】:\n";
            Assert.AreEqual("【示例】周五 18:00 前提交接口联调说明", TodoRichText.ExtractTitle(plain));
        }
    }
}
