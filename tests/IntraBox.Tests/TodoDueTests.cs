using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Modules.Todo;

namespace IntraBox.Tests
{
    [TestClass]
    public class TodoDueTests
    {
        [TestMethod]
        public void IsDatePast_空或当天或未来_不为过期()
        {
            var today = new DateTime(2026, 9, 6);
            Assert.IsFalse(TodoDue.IsDatePast(null, today));
            Assert.IsFalse(TodoDue.IsDatePast(today, today));
            Assert.IsFalse(TodoDue.IsDatePast(today.AddHours(8), today));
            Assert.IsFalse(TodoDue.IsDatePast(today.AddDays(1), today));
        }

        [TestMethod]
        public void IsDatePast_早于当天_为过期()
        {
            var today = new DateTime(2026, 9, 6);
            Assert.IsTrue(TodoDue.IsDatePast(today.AddDays(-1), today));
            Assert.IsTrue(TodoDue.IsDatePast(new DateTime(2026, 9, 5, 23, 59, 0), today));
        }
    }
}
