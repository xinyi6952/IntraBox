using IntraBox.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IntraBox.Tests
{
    [TestClass]
    public class NavFavoriteHelperTests
    {
        [TestMethod]
        public void Contains_空或没有_返回false()
        {
            Assert.IsFalse(NavFavoriteHelper.Contains(null, "todo"));
            Assert.IsFalse(NavFavoriteHelper.Contains(new string[0], "todo"));
            Assert.IsFalse(NavFavoriteHelper.Contains(new[] { "notes" }, ""));
        }

        [TestMethod]
        public void Toggle_追加再取消_保持其余顺序()
        {
            string[] once = NavFavoriteHelper.Toggle(null, "todo");
            CollectionAssert.AreEqual(new[] { "todo" }, once);
            string[] two = NavFavoriteHelper.Toggle(once, "notes");
            CollectionAssert.AreEqual(new[] { "todo", "notes" }, two);
            string[] back = NavFavoriteHelper.Toggle(two, "todo");
            CollectionAssert.AreEqual(new[] { "notes" }, back);
            Assert.IsTrue(NavFavoriteHelper.Contains(two, "notes"));
        }

        [TestMethod]
        public void Normalize_去掉空值与重复()
        {
            string[] n = NavFavoriteHelper.Normalize(new[] { "todo", "", "todo", "notes", "notes" });
            CollectionAssert.AreEqual(new[] { "todo", "notes" }, n);
            Assert.AreEqual(0, NavFavoriteHelper.Normalize(null).Length);
        }
    }
}
