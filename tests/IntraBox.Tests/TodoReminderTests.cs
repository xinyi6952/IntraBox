using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Modules.Todo;

namespace IntraBox.Tests
{
    [TestClass]
    public class TodoReminderTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            TodoReminderService.Stop();
        }

        private static TodoItem Sample(DateTime at)
        {
            return new TodoItem
            {
                Uid = "snooze-uid",
                Completed = false,
                RemindKind = TodoRemindKind.Daily,
                RemindHour = at.Hour,
                RemindMinute = at.Minute,
                RemindTimes = 3,
                RemindIntervalMin = 10,
                LastRemindedAt = at,
                CreatedAt = at
            };
        }

        [TestMethod]
        public void ShouldFire_本轮刚提醒过_同一时段不再弹()
        {
            var at = new DateTime(2026, 9, 6, 17, 35, 0);
            var it = Sample(at);
            Assert.IsFalse(TodoReminderService.ShouldFire(it, at.AddMinutes(1)));
        }

        [TestMethod]
        public void ShouldFire_默认十分钟后再弹第二次()
        {
            var at = new DateTime(2026, 9, 6, 17, 35, 0);
            var it = Sample(at);
            Assert.IsTrue(TodoReminderService.ShouldFire(it, at.AddMinutes(10)));
        }

        [TestMethod]
        public void ShouldFire_三次都过后不再弹()
        {
            var at = new DateTime(2026, 9, 6, 17, 35, 0);
            var it = Sample(at);
            it.LastRemindedAt = at.AddMinutes(20);
            Assert.IsFalse(TodoReminderService.ShouldFire(it, at.AddMinutes(21)));
            Assert.IsFalse(TodoReminderService.ShouldFire(it, at.AddMinutes(35)));
        }

        [TestMethod]
        public void ShouldFire_改时刻后旧提醒不算_新时刻生效()
        {
            var first = new DateTime(2026, 9, 6, 15, 0, 0);
            var it = Sample(first);
            it.LastRemindedAt = first;
            it.RemindHour = 17;
            it.RemindMinute = 5;
            var at = new DateTime(2026, 9, 6, 17, 5, 0);
            Assert.IsTrue(TodoReminderService.ShouldFire(it, at));
        }

        [TestMethod]
        public void ShouldFire_错过时段超过窗口_不补弹()
        {
            var first = new DateTime(2026, 9, 6, 18, 20, 0);
            var it = Sample(first);
            it.LastRemindedAt = null;
            Assert.IsTrue(TodoReminderService.ShouldFire(it, first));
            Assert.IsTrue(TodoReminderService.ShouldFire(it, first.AddMinutes(1)));
            Assert.IsFalse(TodoReminderService.ShouldFire(it, first.AddMinutes(3)));
            Assert.IsFalse(TodoReminderService.ShouldFire(it, first.AddMinutes(28)));
        }

        [TestMethod]
        public void ShouldFire_提醒关闭_不弹()
        {
            var at = new DateTime(2026, 9, 6, 17, 35, 0);
            var it = Sample(at);
            it.LastRemindedAt = null;
            it.RemindKind = TodoRemindKind.Off;
            Assert.IsFalse(TodoReminderService.ShouldFire(it, at));
            TodoReminderService.SnoozeUntil(it.Uid, at.AddMinutes(10));
            Assert.IsFalse(TodoReminderService.ShouldFire(it, at.AddMinutes(10)));
        }

        [TestMethod]
        public void ShouldFire_稍后未到_不弹()
        {
            var at = new DateTime(2026, 9, 6, 17, 35, 0);
            var it = Sample(at);
            TodoReminderService.SnoozeUntil(it.Uid, at.AddMinutes(10));
            Assert.IsFalse(TodoReminderService.ShouldFire(it, at.AddMinutes(5)));
        }

        [TestMethod]
        public void ShouldFire_稍后期到期_即使当天已提醒也弹一次()
        {
            var at = new DateTime(2026, 9, 6, 17, 35, 0);
            var it = Sample(at);
            TodoReminderService.SnoozeUntil(it.Uid, at.AddMinutes(10));
            Assert.IsTrue(TodoReminderService.ShouldFire(it, at.AddMinutes(10)));
            it.LastRemindedAt = at.AddMinutes(10);
            Assert.IsFalse(TodoReminderService.ShouldFire(it, at.AddMinutes(11)));
        }

        private static TodoItem SampleUid(string uid, DateTime at)
        {
            var it = Sample(at);
            it.Uid = uid;
            it.LastRemindedAt = null;
            return it;
        }

        [TestMethod]
        public void Enqueue_同时刻两条_一次只弹一个_关掉再弹下一条_同任务不重复入队()
        {
            var at = new DateTime(2026, 9, 6, 18, 20, 0);
            var a = SampleUid("queue-a", at);
            var b = SampleUid("queue-b", at);
            Assert.IsTrue(TodoReminderService.ShouldFire(a, at));
            Assert.IsTrue(TodoReminderService.ShouldFire(b, at));

            Assert.AreEqual(2, TodoReminderService.EnqueueDueItems(new System.Collections.Generic.List<TodoItem> { a, b }));
            Assert.AreEqual(0, TodoReminderService.EnqueueDueItems(new System.Collections.Generic.List<TodoItem> { a, b }));
            Assert.AreEqual(2, TodoReminderService.QueuedCount);

            TodoItem first;
            TodoItem second;
            int remain;
            Assert.IsTrue(TodoReminderService.TryBeginPrompt(out first, out remain));
            Assert.AreEqual("queue-a", first.Uid);
            Assert.AreEqual(1, remain);
            Assert.IsFalse(TodoReminderService.TryBeginPrompt(out second, out remain));

            TodoReminderService.EndPrompt();
            Assert.IsTrue(TodoReminderService.TryBeginPrompt(out second, out remain));
            Assert.AreEqual("queue-b", second.Uid);
            Assert.AreEqual(0, remain);
            TodoReminderService.EndPrompt();
        }

        [TestMethod]
        public void Enqueue_第一档展示中跨到第二档_新档仍入队()
        {
            var first = new DateTime(2026, 9, 6, 18, 20, 0);
            var it = SampleUid("slot-uid", first);
            Assert.IsTrue(TodoReminderService.ShouldFire(it, first));
            Assert.AreEqual(1, TodoReminderService.EnqueueDueItems(new System.Collections.Generic.List<TodoItem> { it }));

            TodoItem shown;
            int remain;
            Assert.IsTrue(TodoReminderService.TryBeginPrompt(out shown, out remain));
            Assert.AreEqual("slot-uid", shown.Uid);
            Assert.AreEqual(0, remain);

            it.LastRemindedAt = first;
            Assert.IsFalse(TodoReminderService.ShouldFire(it, first.AddMinutes(1)));

            var slot2 = first.AddMinutes(10);
            Assert.IsTrue(TodoReminderService.ShouldFire(it, slot2));
            Assert.AreEqual(1, TodoReminderService.EnqueueDueItems(new System.Collections.Generic.List<TodoItem> { it }));
            Assert.AreEqual(1, TodoReminderService.QueuedCount);

            TodoItem blocked;
            Assert.IsFalse(TodoReminderService.TryBeginPrompt(out blocked, out remain));

            TodoReminderService.EndPrompt();
            TodoItem next;
            Assert.IsTrue(TodoReminderService.TryBeginPrompt(out next, out remain));
            Assert.AreEqual("slot-uid", next.Uid);
            Assert.AreEqual(0, remain);
            TodoReminderService.EndPrompt();
        }

        [TestMethod]
        public void TryCollectTrySave_改提醒时刻_清空已提醒并让新时刻生效()
        {
            var oldAt = new DateTime(2026, 9, 6, 15, 0, 0);
            var before = SampleUid("edit-uid", oldAt);
            before.LastRemindedAt = oldAt;
            before.RemindHour = 15;
            before.RemindMinute = 0;

            var collected = SampleUid("edit-uid", oldAt);
            collected.LastRemindedAt = oldAt;
            collected.RemindHour = 17;
            collected.RemindMinute = 5;

            TodoReminderService.SnoozeUntil(collected.Uid, oldAt.AddHours(3));
            TodoEditorPanel.ApplyTryCollectRemind(before, collected);
            Assert.IsNull(collected.LastRemindedAt);
            TodoEditorPanel.ApplyTrySaveRemind(before, collected);

            var neu = new DateTime(2026, 9, 6, 17, 5, 0);
            Assert.IsTrue(TodoReminderService.ShouldFire(collected, neu));
        }

        [TestMethod]
        public void ParseIndexJson_缺次数与间隔_按三次十分补且不读崩()
        {
            string json = "{"
                + "\"Version\":1,\"SampleSeeded\":true,\"SampleRev\":2,"
                + "\"Items\":[{"
                + "\"Uid\":\"oldjson000000000000000000000001\","
                + "\"Id\":\"T-OLD-001\","
                + "\"Priority\":1,"
                + "\"Completed\":false,"
                + "\"RemindKind\":0,"
                + "\"RemindHour\":15,"
                + "\"RemindMinute\":0,"
                + "\"CreatedAt\":\"2026-01-01T00:00:00\""
                + "}]}";
            var items = TodoStore.ParseIndexJson(json);
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(TodoRemindRepeat.DefaultTimes, items[0].RemindTimes);
            Assert.AreEqual(TodoRemindRepeat.DefaultIntervalMin, items[0].RemindIntervalMin);
            Assert.AreEqual(3, items[0].RemindTimes);
            Assert.AreEqual(10, items[0].RemindIntervalMin);
        }
    }
}
