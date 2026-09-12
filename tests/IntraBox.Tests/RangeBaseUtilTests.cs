using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>设置水位滑块：{x:Static} 绑 int 会打开即崩，须转 double 再赋 RangeBase。</summary>
    [TestClass]
    public class RangeBaseUtilTests
    {
        [TestMethod]
        public void SetValue_装箱int_Maximum失败()
        {
            RunSta(() =>
            {
                var s = new Slider();
                double before = s.Maximum;
                bool threw = false;
                try
                {
                    s.SetValue(RangeBase.MaximumProperty, MemoryTrimPolicy.MaxHighMb);
                }
                catch (Exception)
                {
                    threw = true;
                }
                Assert.IsTrue(threw, "int 装箱赋给 Maximum 应失败");
                Assert.AreEqual(before, s.Maximum);
            });
        }

        [TestMethod]
        public void SetValue_装箱int_Minimum失败()
        {
            RunSta(() =>
            {
                var s = new Slider();
                s.Maximum = 2048d;
                bool threw = false;
                try
                {
                    s.SetValue(RangeBase.MinimumProperty, MemoryTrimPolicy.MinHighMb);
                }
                catch (Exception)
                {
                    threw = true;
                }
                Assert.IsTrue(threw, "int 装箱赋给 Minimum 应失败");
                Assert.AreEqual(0d, s.Minimum);
            });
        }

        [TestMethod]
        public void Xaml_xStatic绑int_解析失败()
        {
            RunSta(() =>
            {
                string xaml =
                    "<Slider xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                    "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                    "xmlns:core='clr-namespace:IntraBox.Core;assembly=IntraBox' " +
                    "Maximum='{x:Static core:MemoryTrimPolicy.MaxHighMb}'/>";
                try
                {
                    XamlReader.Parse(xaml);
                    Assert.Fail("x:Static int 赋给 Maximum 应在解析时失败");
                }
                catch (AssertFailedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    string msg = ex.ToString();
                    Assert.IsTrue(msg.IndexOf("2048", StringComparison.Ordinal) >= 0
                        || msg.IndexOf("Maximum", StringComparison.Ordinal) >= 0, msg);
                }
            });
        }

        [TestMethod]
        public void Xaml_字面量_可设高水位范围()
        {
            RunSta(() =>
            {
                var s = (Slider)XamlReader.Parse(
                    "<Slider xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Maximum='2048' Minimum='80'/>");
                Assert.AreEqual(80d, s.Minimum);
                Assert.AreEqual(2048d, s.Maximum);
            });
        }

        [TestMethod]
        public void SetBounds_高水位_80至2048()
        {
            RunSta(() =>
            {
                var s = new Slider();
                RangeBaseUtil.SetBounds(s, MemoryTrimPolicy.MinHighMb, MemoryTrimPolicy.MaxHighMb);
                Assert.AreEqual((double)MemoryTrimPolicy.MinHighMb, s.Minimum);
                Assert.AreEqual((double)MemoryTrimPolicy.MaxHighMb, s.Maximum);
                Assert.IsTrue(s.Value >= s.Minimum);
                Assert.IsTrue(s.Value <= s.Maximum);
            });
        }

        [TestMethod]
        public void SetBounds_低水位_随高水位下调上限()
        {
            RunSta(() =>
            {
                var s = new Slider();
                RangeBaseUtil.SetBounds(s, MemoryTrimPolicy.MinLowMb, MemoryTrimPolicy.MaxLowMb);
                s.Value = 500;
                RangeBaseUtil.SetBounds(s, MemoryTrimPolicy.MinLowMb, 200);
                Assert.AreEqual(50d, s.Minimum);
                Assert.AreEqual(200d, s.Maximum);
                Assert.AreEqual(200d, s.Value);
            });
        }

        private static void RunSta(Action action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                action();
                return;
            }
            Exception error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
