using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    [TestClass]
    public class EyeCareHelperTests
    {
        [TestMethod]
        public void Clamp_色温与亮度边界()
        {
            Assert.AreEqual(1700, EyeCareHelper.ClampKelvin(100));
            Assert.AreEqual(6500, EyeCareHelper.ClampKelvin(9000));
            Assert.AreEqual(3400, EyeCareHelper.NormalizeKelvin(0));
            Assert.AreEqual(20, EyeCareHelper.ClampBrightness(1));
            Assert.AreEqual(100, EyeCareHelper.ClampBrightness(200));
            Assert.AreEqual(100, EyeCareHelper.NormalizeBrightness(0));
        }

        [TestMethod]
        public void KelvinToRgb_6500接近白_1900蓝很少()
        {
            byte r, g, b;
            EyeCareHelper.KelvinToRgb(6500, out r, out g, out b);
            Assert.IsTrue(r > 240 && g > 240 && b > 240, r + "," + g + "," + b);

            byte r2, g2, b2;
            EyeCareHelper.KelvinToRgb(1900, out r2, out g2, out b2);
            Assert.AreEqual(255, r2);
            Assert.IsTrue(b2 < 50, "blue=" + b2);
            Assert.IsTrue(g2 < r2);
        }

        [TestMethod]
        public void BuildRamp_白天100与深夜50_曲线不同()
        {
            var r1 = new ushort[256];
            var g1 = new ushort[256];
            var b1 = new ushort[256];
            EyeCareHelper.BuildRamp(6500, 100, r1, g1, b1);
            Assert.AreEqual(0, r1[0]);
            Assert.AreEqual(128 * 256, r1[128]);

            var r2 = new ushort[256];
            var g2 = new ushort[256];
            var b2 = new ushort[256];
            EyeCareHelper.BuildRamp(1900, 50, r2, g2, b2);
            Assert.IsTrue(b2[128] < b1[128]);
            Assert.IsTrue(r2[128] < r1[128]);
        }

        [TestMethod]
        public void BuildRamp_极暖极暗_通道仍递增()
        {
            var r = new ushort[256];
            var g = new ushort[256];
            var b = new ushort[256];
            EyeCareHelper.BuildRamp(1700, 20, r, g, b);
            for (int i = 1; i < 256; i++)
            {
                Assert.IsTrue(r[i] >= r[i - 1], "red " + i);
                Assert.IsTrue(g[i] >= g[i - 1], "green " + i);
                Assert.IsTrue(b[i] >= b[i - 1], "blue " + i);
            }
        }

        [TestMethod]
        public void BuildColorMatrix_白天近单位_深夜压蓝()
        {
            var day = new float[25];
            EyeCareHelper.BuildColorMatrix(6500, 100, day);
            Assert.IsTrue(day[0] > 0.94f && day[6] > 0.94f && day[12] > 0.94f);
            Assert.AreEqual(1f, day[18]);
            Assert.AreEqual(1f, day[24]);

            var night = new float[25];
            EyeCareHelper.BuildColorMatrix(1900, 50, night);
            Assert.IsTrue(night[0] > night[12]);
            Assert.IsTrue(night[12] < day[12]);
            Assert.IsTrue(night[0] < day[0]);
        }

        [TestMethod]
        public void TryParseHm_合法与非法()
        {
            int m;
            Assert.IsTrue(EyeCareHelper.TryParseHm("19:00", out m));
            Assert.AreEqual(19 * 60, m);
            Assert.IsTrue(EyeCareHelper.TryParseHm("7:05", out m));
            Assert.AreEqual(7 * 60 + 5, m);
            Assert.IsFalse(EyeCareHelper.TryParseHm("24:00", out m));
            Assert.IsFalse(EyeCareHelper.TryParseHm("abc", out m));
            Assert.AreEqual("07:05", EyeCareHelper.FormatHm(7 * 60 + 5));
        }

        [TestMethod]
        public void IsInSchedule_跨午夜与当天()
        {
            var day = new DateTime(2026, 9, 17, 20, 0, 0);
            var night = new DateTime(2026, 9, 17, 2, 0, 0);
            var noon = new DateTime(2026, 9, 17, 12, 0, 0);
            Assert.IsTrue(EyeCareHelper.IsInSchedule(day, "19:00", "07:00"));
            Assert.IsTrue(EyeCareHelper.IsInSchedule(night, "19:00", "07:00"));
            Assert.IsFalse(EyeCareHelper.IsInSchedule(noon, "19:00", "07:00"));
            var atStart = new DateTime(2026, 9, 17, 19, 0, 0);
            var atEnd = new DateTime(2026, 9, 17, 7, 0, 0);
            var beforeEnd = new DateTime(2026, 9, 17, 6, 59, 0);
            Assert.IsTrue(EyeCareHelper.IsInSchedule(atStart, "19:00", "07:00"));
            Assert.IsTrue(EyeCareHelper.IsInSchedule(beforeEnd, "19:00", "07:00"));
            Assert.IsFalse(EyeCareHelper.IsInSchedule(atEnd, "19:00", "07:00"));
            Assert.IsTrue(EyeCareHelper.IsInSchedule(noon, "09:00", "18:00"));
            Assert.IsFalse(EyeCareHelper.IsInSchedule(night, "09:00", "18:00"));
            Assert.IsTrue(EyeCareHelper.IsInSchedule(noon, "12:00", "12:00"));
        }

        [TestMethod]
        public void MatchKnownTool_常见护眼进程()
        {
            Assert.AreEqual("f.lux", EyeCareHelper.MatchKnownTool("flux"));
            Assert.AreEqual("f.lux", EyeCareHelper.MatchKnownTool("FLUX.EXE"));
            Assert.AreEqual("CareUEyes", EyeCareHelper.MatchKnownTool("CareUEyes"));
            Assert.AreEqual("Redshift", EyeCareHelper.MatchKnownTool("gtk-redshift"));
            Assert.IsNull(EyeCareHelper.MatchKnownTool("notepad"));
            Assert.IsNull(EyeCareHelper.MatchKnownTool("IntraBox"));
            Assert.IsNull(EyeCareHelper.MatchKnownTool(""));
        }

        [TestMethod]
        public void CollectConflictLabels_去重并含夜间模式()
        {
            var labels = EyeCareHelper.CollectConflictLabels(
                new[] { "flux.exe", "FLUX", "notepad", "careueyes" }, true);
            Assert.AreEqual(3, labels.Count);
            Assert.IsTrue(labels.Contains("f.lux"));
            Assert.IsTrue(labels.Contains("CareUEyes"));
            Assert.IsTrue(labels.Contains("系统夜间模式"));
            Assert.IsNull(EyeCareHelper.BuildConflictPrompt(new string[0]));
            string prompt = EyeCareHelper.BuildConflictPrompt(labels);
            Assert.IsTrue(prompt.IndexOf("f.lux", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(prompt.IndexOf("仍要开启", StringComparison.Ordinal) >= 0);
        }

        [TestMethod]
        public void LooksLikeNightLightOn_常见字节()
        {
            Assert.IsFalse(EyeCareHelper.LooksLikeNightLightOn(null));
            Assert.IsFalse(EyeCareHelper.LooksLikeNightLightOn(new byte[10]));
            var off = new byte[24];
            off[18] = 0x13;
            Assert.IsFalse(EyeCareHelper.LooksLikeNightLightOn(off));
            var on = new byte[24];
            on[18] = 0x15;
            Assert.IsTrue(EyeCareHelper.LooksLikeNightLightOn(on));
        }

        [TestMethod]
        public void ValidateSegments_重叠与合法边界()
        {
            var ok = EyeCareHelper.CreateDefaultSegments();
            Assert.IsNull(EyeCareHelper.ValidateSegments(ok));
            Assert.IsTrue(EyeCareHelper.IsDefaultSegments(ok));
            Assert.IsFalse(EyeCareHelper.HasCoverageGap(ok));

            var clash = new[]
            {
                EyeCareHelper.MakeSegment("21:00", "07:00", 1900, 100),
                EyeCareHelper.MakeSegment("06:00", "09:00", 3400, 100)
            };
            string err = EyeCareHelper.ValidateSegments(clash);
            Assert.IsNotNull(err);
            Assert.IsTrue(err.IndexOf("重叠", StringComparison.Ordinal) >= 0);

            var abut = new[]
            {
                EyeCareHelper.MakeSegment("09:00", "13:00", 3400, 100),
                EyeCareHelper.MakeSegment("13:00", "18:00", 6500, 100)
            };
            Assert.IsNull(EyeCareHelper.ValidateSegments(abut));
            Assert.IsNotNull(EyeCareHelper.ValidateSegments(new EyeCareSegment[0]));
        }

        [TestMethod]
        public void TryPickSegment_按时刻选段()
        {
            var segs = EyeCareHelper.CreateDefaultSegments();
            EyeCareSegment hit;
            Assert.IsTrue(EyeCareHelper.TryPickSegment(new DateTime(2026, 9, 17, 10, 0, 0), segs, out hit));
            Assert.AreEqual(6500, hit.Kelvin);
            Assert.IsTrue(EyeCareHelper.TryPickSegment(new DateTime(2026, 9, 17, 18, 0, 0), segs, out hit));
            Assert.AreEqual(3400, hit.Kelvin);
            Assert.IsTrue(EyeCareHelper.TryPickSegment(new DateTime(2026, 9, 17, 22, 0, 0), segs, out hit));
            Assert.AreEqual(1900, hit.Kelvin);
            Assert.IsTrue(EyeCareHelper.TryPickSegment(new DateTime(2026, 9, 17, 2, 0, 0), segs, out hit));
            Assert.AreEqual(1900, hit.Kelvin);

            var only = new[] { EyeCareHelper.MakeSegment("09:00", "13:00", 3400, 80) };
            Assert.IsFalse(EyeCareHelper.TryPickSegment(new DateTime(2026, 9, 17, 8, 0, 0), only, out hit));
            Assert.IsTrue(EyeCareHelper.TryPickSegment(new DateTime(2026, 9, 17, 9, 0, 0), only, out hit));
            Assert.IsFalse(EyeCareHelper.TryPickSegment(new DateTime(2026, 9, 17, 13, 0, 0), only, out hit));
        }

        [TestMethod]
        public void CreateDefaultSegments_含白天晚间深夜且默认启用()
        {
            var segs = EyeCareHelper.CreateDefaultSegments();
            Assert.AreEqual(3, segs.Count);
            Assert.AreEqual("07:00", segs[0].StartHm);
            Assert.AreEqual("17:00", segs[0].EndHm);
            Assert.AreEqual(6500, segs[0].Kelvin);
            Assert.IsFalse(segs[0].Disabled);
            Assert.AreEqual("白天", EyeCareHelper.MatchPresetName(segs[0].StartHm, segs[0].EndHm));
            Assert.AreEqual("晚间", EyeCareHelper.MatchPresetName(segs[1].StartHm, segs[1].EndHm));
            Assert.AreEqual("深夜", EyeCareHelper.MatchPresetName(segs[2].StartHm, segs[2].EndHm));
            Assert.AreEqual(1900, segs[2].Kelvin);
            Assert.IsFalse(segs[1].Disabled);
            Assert.IsFalse(segs[2].Disabled);
        }

        [TestMethod]
        public void DisabledSegment_不选中且不参与重叠()
        {
            var segs = EyeCareHelper.CreateDefaultSegments();
            segs[0].Disabled = true;
            EyeCareSegment hit;
            Assert.IsFalse(EyeCareHelper.TryPickSegment(new DateTime(2026, 9, 17, 10, 0, 0), segs, out hit));
            Assert.IsTrue(EyeCareHelper.TryPickSegment(new DateTime(2026, 9, 17, 18, 0, 0), segs, out hit));
            Assert.AreEqual(3400, hit.Kelvin);
            Assert.IsTrue(EyeCareHelper.HasCoverageGap(segs));
            Assert.IsFalse(EyeCareHelper.IsDefaultSegments(segs));
            Assert.IsTrue(segs[0].Clone().Disabled);

            var overlapOff = new[]
            {
                EyeCareHelper.MakeSegment("09:00", "13:00", 3400, 100),
                EyeCareHelper.MakeSegment("10:00", "12:00", 1900, 100)
            };
            overlapOff[0].Disabled = true;
            Assert.IsNull(EyeCareHelper.ValidateSegments(overlapOff));

            var overlapOn = EyeCareHelper.CloneSegments(overlapOff);
            overlapOn[0].Disabled = false;
            string err = EyeCareHelper.ValidateSegments(overlapOn);
            Assert.IsNotNull(err);
            Assert.IsTrue(err.IndexOf("重叠", StringComparison.Ordinal) >= 0);
        }

        [TestMethod]
        public void EnsureSegments_新用户默认三段_旧定时迁一段()
        {
            var fresh = new AppSettings();
            Assert.IsTrue(EyeCareHelper.EnsureSegments(fresh, false));
            Assert.IsTrue(EyeCareHelper.IsDefaultSegments(fresh.EyeCareSegments));
            Assert.IsFalse(EyeCareHelper.EnsureSegments(fresh, false));

            var unused = new AppSettings();
            Assert.IsTrue(EyeCareHelper.EnsureSegments(unused, true));
            Assert.IsTrue(EyeCareHelper.IsDefaultSegments(unused.EyeCareSegments));

            var oldOn = new AppSettings();
            oldOn.EyeCareSchedule = true;
            oldOn.EyeCareStartHm = "19:00";
            oldOn.EyeCareEndHm = "07:00";
            oldOn.EyeCareKelvin = 3400;
            oldOn.EyeCareBrightness = 90;
            Assert.IsTrue(EyeCareHelper.EnsureSegments(oldOn, true));
            Assert.AreEqual(1, oldOn.EyeCareSegments.Count);
            Assert.AreEqual("19:00", oldOn.EyeCareSegments[0].StartHm);
            Assert.AreEqual(90, oldOn.EyeCareSegments[0].Brightness);

            var oldAll = new AppSettings();
            oldAll.EyeCareEnabled = true;
            oldAll.EyeCareSchedule = false;
            oldAll.EyeCareKelvin = 4000;
            Assert.IsTrue(EyeCareHelper.EnsureSegments(oldAll, true));
            Assert.AreEqual(1, oldAll.EyeCareSegments.Count);
            Assert.AreEqual("全天", EyeCareHelper.FormatRange(oldAll.EyeCareSegments[0].StartHm, oldAll.EyeCareSegments[0].EndHm));
            Assert.AreEqual(4000, oldAll.EyeCareSegments[0].Kelvin);

            var placeholder = new AppSettings();
            placeholder.EyeCareEnabled = false;
            placeholder.EyeCareSegments = new System.Collections.Generic.List<EyeCareSegment>
            {
                EyeCareHelper.MakeSegment("00:00", "00:00", 3400, 100)
            };
            Assert.IsTrue(EyeCareHelper.EnsureSegments(placeholder, false));
            Assert.IsTrue(EyeCareHelper.IsDefaultSegments(placeholder.EyeCareSegments));
        }

        [TestMethod]
        public void MatchPresetName_按起止时刻()
        {
            Assert.AreEqual("白天", EyeCareHelper.MatchPresetName("07:00", "17:00"));
            Assert.AreEqual("晚间", EyeCareHelper.MatchPresetName("17:00", "21:00"));
            Assert.AreEqual("深夜", EyeCareHelper.MatchPresetName("21:00", "07:00"));
            Assert.AreEqual("自定义", EyeCareHelper.MatchPresetName("09:00", "13:00"));
        }

        [TestMethod]
        public void FormatPauseClock_一小时与不足一分()
        {
            Assert.AreEqual("60:00", EyeCareHelper.FormatPauseClock(TimeSpan.FromHours(1)));
            Assert.AreEqual("00:59", EyeCareHelper.FormatPauseClock(TimeSpan.FromSeconds(59)));
            Assert.AreEqual("00:00", EyeCareHelper.FormatPauseClock(TimeSpan.Zero));
        }

        [TestMethod]
        public void FormatRestStatus_未开与未改色与计时中()
        {
            string off = EyeCareHelper.FormatRestStatus(false, false, TimeSpan.Zero);
            Assert.IsTrue(off.IndexOf("未开", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(off.IndexOf("改色时开始", StringComparison.Ordinal) >= 0);

            string wait = EyeCareHelper.FormatRestStatus(true, false, TimeSpan.FromMinutes(20));
            Assert.IsTrue(wait.IndexOf("未改色", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(wait.IndexOf("滤镜改色后开始计", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(wait.IndexOf("预览提醒", StringComparison.Ordinal) >= 0);

            string frozen = EyeCareHelper.FormatRestStatus(true, false, TimeSpan.FromMinutes(5));
            Assert.IsTrue(frozen.IndexOf("计时暂停", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(frozen.IndexOf("05:00", StringComparison.Ordinal) >= 0);

            string on = EyeCareHelper.FormatRestStatus(true, true, TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(30));
            Assert.IsTrue(on.IndexOf("计时中", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(on.IndexOf("12:30", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(on.IndexOf("右下角", StringComparison.Ordinal) >= 0);
        }

        [TestMethod]
        public void ShouldRunRestClock_关闭暂停未改色都不计()
        {
            Assert.IsFalse(EyeCareHelper.ShouldRunRestClock(true, false, false, true));
            Assert.IsFalse(EyeCareHelper.ShouldRunRestClock(true, true, true, true));
            Assert.IsFalse(EyeCareHelper.ShouldRunRestClock(true, true, false, false));
            Assert.IsFalse(EyeCareHelper.ShouldRunRestClock(false, true, false, true));
            Assert.IsTrue(EyeCareHelper.ShouldRunRestClock(true, true, false, true));
        }
    }
}
