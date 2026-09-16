using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class FormHintTests
    {
        [TestMethod]
        public void IsError_校验与失败为真_已保存为假()
        {
            Assert.IsTrue(FormHint.IsError("请填写名称"));
            Assert.IsTrue(FormHint.IsError("输入不能为空"));
            Assert.IsTrue(FormHint.IsError("保存失败：磁盘只读"));
            Assert.IsTrue(FormHint.IsError("请输入主机"));
            Assert.IsTrue(FormHint.IsError("至少选择一个工具"));
            Assert.IsTrue(FormHint.IsError("请选基准日"));
            Assert.IsTrue(FormHint.IsError("两次输入不一致"));
            Assert.IsTrue(FormHint.IsError("颜色格式应为 #RRGGBB"));
            Assert.IsFalse(FormHint.IsError("已保存"));
            Assert.IsFalse(FormHint.IsError("已复制"));
            Assert.IsFalse(FormHint.IsError(""));
            Assert.IsFalse(FormHint.IsError(null));
            Assert.IsTrue(FormHint.IsError("已锁定，只能查看"));
            Assert.IsTrue(FormHint.IsOk("已保存"));
            Assert.IsTrue(FormHint.IsOk("成功"));
            Assert.IsFalse(FormHint.IsOk("已锁定，只能查看"));
            Assert.IsTrue(FormHint.IsOk("当前：已阻止睡眠，剩余 10 秒"));
        }

        [TestMethod]
        public void IsError_说明句为假_拦截句为真()
        {
            Assert.IsFalse(FormHint.IsError("系统变量 5 个，只读。"));
            Assert.IsFalse(FormHint.IsError("进程变量 3 个，只读。"));
            Assert.IsFalse(FormHint.IsError("填写名称和值后保存。名称为空不能保存。"));
            Assert.IsFalse(FormHint.IsError("编步骤；REST 可「从 curl 新建」。【示例】只读。"));
            Assert.IsTrue(FormHint.IsError("系统与进程变量只读，只能改用户级。"));
            Assert.IsTrue(FormHint.IsError("步骤超过 5 个，只读不能保存。"));
            Assert.IsTrue(FormHint.IsError("【示例】只读，不能保存"));
            Assert.IsTrue(FormHint.IsError("未定义变量 token"));
            Assert.IsTrue(FormHint.IsError("请填写名称"));
            Assert.IsTrue(FormHint.IsError("未填写值，无法新建。"));
            Assert.IsFalse(FormHint.IsError("运行成功（已记入手动成功，允许自动跑）"));
            Assert.IsTrue(FormHint.IsOk("已保存用户变量 PATH。新开的终端才生效。"));
        }
    }
}
