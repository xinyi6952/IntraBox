using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class ExceptionHelperTests
    {
        [TestMethod]
        public void FormatOcrFailure_TIE包DllNotFound_含VC与详情()
        {
            var inner = new DllNotFoundException("Unable to load DLL 'tesseract41.dll': The specified module could not be found.");
            var tie = new TargetInvocationException("调用的目标发生了异常。", inner);
            string text = ExceptionHelper.FormatOcrFailure(tie);
            Assert.IsTrue(text.IndexOf("Visual C++ 2015-2019", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(text.IndexOf("tesseract41", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsFalse(text.Trim().Equals("调用的目标发生了异常。", StringComparison.Ordinal));
        }

        [TestMethod]
        public void FormatOcrFailure_TIE包引擎原文_不能只剩包装句()
        {
            var inner = new InvalidOperationException("Failed to initialise tesseract engine.");
            var tie = new TargetInvocationException("调用的目标发生了异常", inner);
            string text = ExceptionHelper.FormatOcrFailure(tie);
            Assert.IsTrue(text.IndexOf("Failed to initialise tesseract engine.", StringComparison.Ordinal) >= 0);
            Assert.IsFalse(text.IndexOf("调用的目标发生了异常", StringComparison.Ordinal) >= 0);
        }

        [TestMethod]
        public void FormatOcrFailure_Aggregate再包TIE_仍能看到Dll()
        {
            var inner = new DllNotFoundException("tesseract41.dll");
            var tie = new TargetInvocationException("调用的目标发生了异常", inner);
            var ae = new AggregateException(tie);
            string text = ExceptionHelper.FormatOcrFailure(ae);
            Assert.IsTrue(text.IndexOf("Visual C++ 2015-2019", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(text.IndexOf("tesseract41", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [TestMethod]
        public void FormatOcrFailure_缺tessdata中文原样()
        {
            var ex = new InvalidOperationException("缺少 tessdata 目录，无法识别文字。");
            Assert.AreEqual("缺少 tessdata 目录，无法识别文字。", ExceptionHelper.FormatOcrFailure(ex));
        }
    }
}
