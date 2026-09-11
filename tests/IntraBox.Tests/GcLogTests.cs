using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class GcLogTests
    {
        [TestMethod]
        public void FormatLine_run含trim与内存前后()
        {
            var before = new MemoryUsage
            {
                WorkingSetBytes = 200L * 1024 * 1024,
                PrivateBytes = 300L * 1024 * 1024,
                ManagedBytes = 40L * 1024 * 1024
            };
            var after = new MemoryUsage
            {
                WorkingSetBytes = 90L * 1024 * 1024,
                PrivateBytes = 280L * 1024 * 1024,
                ManagedBytes = 8L * 1024 * 1024
            };
            string line = GcHelper.FormatLine(
                new DateTime(2026, 9, 11, 8, 31, 0, 123),
                false, "manual", null, true, 45, before, after);
            StringAssert.Contains(line, "2026-09-11 08:31:00.123");
            StringAssert.Contains(line, "GC run reason=manual");
            StringAssert.Contains(line, "trim=1");
            StringAssert.Contains(line, "elapsed=45ms");
            StringAssert.Contains(line, "ws=200->90");
            StringAssert.Contains(line, "private=300->280");
            StringAssert.Contains(line, "managed=40->8");
            Assert.IsFalse(line.Contains("\n"));
            Assert.IsFalse(line.Contains("\r"));
        }

        [TestMethod]
        public void FormatLine_skip记闸门()
        {
            string line = GcHelper.FormatLine(DateTime.Now, true, "switch-light:timestamp", "gate", false, 0, null, null);
            StringAssert.Contains(line, "GC skip reason=switch-light:timestamp");
            StringAssert.Contains(line, "why=gate");
            Assert.IsFalse(line.Contains("trim="));
        }

        [TestMethod]
        public void SanitizeReason_空值换行与截断()
        {
            Assert.AreEqual("unspecified", GcHelper.SanitizeReason(null));
            Assert.AreEqual("unspecified", GcHelper.SanitizeReason(""));
            Assert.AreEqual("a b c", GcHelper.SanitizeReason("a\nb\tc"));
            Assert.AreEqual(80, GcHelper.SanitizeReason(new string('x', 100)).Length);
        }
    }
}
