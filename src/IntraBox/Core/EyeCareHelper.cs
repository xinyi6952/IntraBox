using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>
    /// 护眼色温/亮度 → 伽马曲线，以及每日时段判断。不碰显示器。
    /// 色温公式为 Tanner Helland 近似（与 f.lux 同类）。
    /// </summary>
    public static class EyeCareHelper
    {
        public const int MinKelvin = 1700;
        public const int MaxKelvin = 6500;
        public const int DefaultKelvin = 3400;
        public const int DayKelvin = 6500;
        public const int EveningKelvin = 3400;
        public const int NightKelvin = 1900;
        public const int MinBrightness = 20;
        public const int MaxBrightness = 100;
        public const int DefaultBrightness = 100;
        public const string DefaultStart = "19:00";
        public const string DefaultEnd = "07:00";
        public const int MaxSegments = 8;
        public const string DayStart = "07:00";
        public const string DayEnd = "17:00";
        public const string EveningStart = "17:00";
        public const string EveningEnd = "21:00";
        public const string NightStart = "21:00";
        public const string NightEnd = "07:00";
        public const string PresetDayName = "白天";
        public const string PresetEveningName = "晚间";
        public const string PresetNightName = "深夜";
        public const string PresetCustomName = "自定义";

        public static int ClampKelvin(int k)
        {
            if (k < MinKelvin) return MinKelvin;
            if (k > MaxKelvin) return MaxKelvin;
            return k;
        }

        public static int ClampBrightness(int p)
        {
            if (p < MinBrightness) return MinBrightness;
            if (p > MaxBrightness) return MaxBrightness;
            return p;
        }

        /// <summary>缺字段或 0 按晚间预设补齐，不改用户已保存的合法值。</summary>
        public static int NormalizeKelvin(int k)
        {
            if (k <= 0) return DefaultKelvin;
            return ClampKelvin(k);
        }

        public static int NormalizeBrightness(int p)
        {
            if (p <= 0) return DefaultBrightness;
            return ClampBrightness(p);
        }

        /// <summary>6500K 接近白；越低红越多、蓝越少。</summary>
        public static void KelvinToRgb(int kelvin, out byte r, out byte g, out byte b)
        {
            double k = ClampKelvin(kelvin) / 100.0;
            double rd, gd, bd;
            if (k <= 66)
            {
                rd = 255;
                gd = 99.4708025861 * Math.Log(k) - 161.1195681661;
            }
            else
            {
                rd = 329.698727446 * Math.Pow(k - 60, -0.1332047592);
                gd = 288.1221695283 * Math.Pow(k - 60, -0.0755148492);
            }

            if (k >= 66) bd = 255;
            else if (k <= 19) bd = 0;
            else bd = 138.5177312231 * Math.Log(k - 10) - 305.0447927307;

            r = ToByte(rd);
            g = ToByte(gd);
            b = ToByte(bd);
        }

        /// <summary>256 点查找表，identity 为 i*256。brightnessPercent 20–100。</summary>
        public static void BuildRamp(int kelvin, int brightnessPercent, ushort[] red, ushort[] green, ushort[] blue)
        {
            if (red == null || green == null || blue == null)
                throw new ArgumentNullException();
            if (red.Length < 256 || green.Length < 256 || blue.Length < 256)
                throw new ArgumentException("ramp length");
            byte r, g, b;
            KelvinToRgb(kelvin, out r, out g, out b);
            double bright = NormalizeBrightness(brightnessPercent) / 100.0;
            double rs = (r / 255.0) * bright;
            double gs = (g / 255.0) * bright;
            double bs = (b / 255.0) * bright;
            for (int i = 0; i < 256; i++)
            {
                red[i] = ToRamp(i, rs);
                green[i] = ToRamp(i, gs);
                blue[i] = ToRamp(i, bs);
            }
            EnsureIncreasing(red);
            EnsureIncreasing(green);
            EnsureIncreasing(blue);
        }

        /// <summary>5×5 行主序校色矩阵，供 Magnification 全屏校色。对角为 RGB 缩放。</summary>
        public static void BuildColorMatrix(int kelvin, int brightnessPercent, float[] matrix)
        {
            if (matrix == null || matrix.Length < 25)
                throw new ArgumentException("matrix");
            FillIdentityColorMatrix(matrix);
            byte r, g, b;
            KelvinToRgb(kelvin, out r, out g, out b);
            float bright = NormalizeBrightness(brightnessPercent) / 100f;
            matrix[0] = (r / 255f) * bright;
            matrix[6] = (g / 255f) * bright;
            matrix[12] = (b / 255f) * bright;
        }

        public static void FillIdentityColorMatrix(float[] matrix)
        {
            if (matrix == null || matrix.Length < 25)
                throw new ArgumentException("matrix");
            for (int i = 0; i < 25; i++) matrix[i] = 0;
            matrix[0] = 1;
            matrix[6] = 1;
            matrix[12] = 1;
            matrix[18] = 1;
            matrix[24] = 1;
        }

        public static bool TryParseHm(string text, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            string[] parts = t.Split(':');
            if (parts.Length != 2) return false;
            int h, m;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            minutes = h * 60 + m;
            return true;
        }

        public static string FormatHm(int minutes)
        {
            int n = ((minutes % 1440) + 1440) % 1440;
            int h = n / 60;
            int m = n % 60;
            return h.ToString("00", CultureInfo.InvariantCulture) + ":" + m.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 开始&lt;结束：当天闭开区间；开始&gt;结束：跨午夜；相等视为全天。
        /// </summary>
        public static bool IsInSchedule(DateTime local, int startMin, int endMin)
        {
            int t = local.Hour * 60 + local.Minute;
            if (startMin == endMin) return true;
            if (startMin < endMin) return t >= startMin && t < endMin;
            return t >= startMin || t < endMin;
        }

        public static bool IsInSchedule(DateTime local, string startHm, string endHm)
        {
            int s, e;
            if (!TryParseHm(string.IsNullOrWhiteSpace(startHm) ? DefaultStart : startHm, out s))
                s = 19 * 60;
            if (!TryParseHm(string.IsNullOrWhiteSpace(endHm) ? DefaultEnd : endHm, out e))
                e = 7 * 60;
            return IsInSchedule(local, s, e);
        }

        public static EyeCareSegment MakeSegment(string startHm, string endHm, int kelvin, int brightness)
        {
            return new EyeCareSegment
            {
                StartHm = startHm,
                EndHm = endHm,
                Kelvin = ClampKelvin(kelvin),
                Brightness = ClampBrightness(brightness)
            };
        }

        public static List<EyeCareSegment> CreateDefaultSegments()
        {
            return new List<EyeCareSegment>
            {
                MakeSegment(DayStart, DayEnd, DayKelvin, DefaultBrightness),
                MakeSegment(EveningStart, EveningEnd, EveningKelvin, DefaultBrightness),
                MakeSegment(NightStart, NightEnd, NightKelvin, DefaultBrightness)
            };
        }

        public static List<EyeCareSegment> CloneSegments(IList<EyeCareSegment> src)
        {
            var list = new List<EyeCareSegment>();
            if (src == null) return list;
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i] != null) list.Add(src[i].Clone());
            }
            return list;
        }

        /// <summary>无错误返回 null。</summary>
        public static string ValidateSegments(IList<EyeCareSegment> list)
        {
            if (list == null || list.Count == 0) return "请至少保留一段。";
            if (list.Count > MaxSegments) return "最多 " + MaxSegments + " 段。";
            var starts = new int[list.Count];
            var ends = new int[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                EyeCareSegment seg = list[i];
                if (seg == null) return "时段无效。";
                int a, b;
                if (!TryParseHm(seg.StartHm, out a) || !TryParseHm(seg.EndHm, out b))
                    return "时段时间无效，请填写 HH:mm。";
                if (a == b && list.Count > 1)
                    return "多段时开始与结束不能相同。";
                if (seg.Kelvin < MinKelvin || seg.Kelvin > MaxKelvin)
                    return "色温须在 1700–6500 K。";
                if (seg.Brightness < MinBrightness || seg.Brightness > MaxBrightness)
                    return "亮度须在 20–100%。";
                starts[i] = a;
                ends[i] = b;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Disabled) continue;
                for (int j = i + 1; j < list.Count; j++)
                {
                    if (list[j].Disabled) continue;
                    if (RangesOverlap(starts[i], ends[i], starts[j], ends[j]))
                    {
                        return "时段「" + FormatRange(starts[i], ends[i]) + "」与「"
                            + FormatRange(starts[j], ends[j]) + "」重叠。";
                    }
                }
            }
            return null;
        }

        public static bool RangesOverlap(int startA, int endA, int startB, int endB)
        {
            bool[] a = new bool[1440];
            bool[] b = new bool[1440];
            MarkRange(a, startA, endA);
            MarkRange(b, startB, endB);
            for (int i = 0; i < 1440; i++)
            {
                if (a[i] && b[i]) return true;
            }
            return false;
        }

        public static bool TryPickSegment(DateTime local, IList<EyeCareSegment> list, out EyeCareSegment hit)
        {
            hit = null;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                EyeCareSegment seg = list[i];
                if (seg == null) continue;
                if (seg.Disabled) continue;
                if (!IsInSchedule(local, seg.StartHm, seg.EndHm)) continue;
                hit = seg;
                return true;
            }
            return false;
        }

        public static bool IsDefaultSegments(IList<EyeCareSegment> list)
        {
            var def = CreateDefaultSegments();
            if (list == null || list.Count != def.Count) return false;
            for (int i = 0; i < def.Count; i++)
            {
                EyeCareSegment a = list[i];
                EyeCareSegment b = def[i];
                if (a == null) return false;
                if (!string.Equals(NormalizeHm(a.StartHm), b.StartHm, StringComparison.Ordinal)) return false;
                if (!string.Equals(NormalizeHm(a.EndHm), b.EndHm, StringComparison.Ordinal)) return false;
                if (ClampKelvin(a.Kelvin) != b.Kelvin) return false;
                if (ClampBrightness(a.Brightness) != b.Brightness) return false;
                if (a.Disabled) return false;
            }
            return true;
        }

        public static string FormatRange(string startHm, string endHm)
        {
            int a, b;
            if (!TryParseHm(startHm, out a) || !TryParseHm(endHm, out b))
                return (startHm ?? "") + "–" + (endHm ?? "");
            return FormatRange(a, b);
        }

        public static string FormatRange(int startMin, int endMin)
        {
            if (startMin == endMin) return "全天";
            return FormatHm(startMin) + "–" + FormatHm(endMin);
        }

        public static string FormatPauseClock(TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero) return "00:00";
            int sec = (int)Math.Floor(remaining.TotalSeconds);
            if (sec < 0) sec = 0;
            int mm = sec / 60;
            int ss = sec % 60;
            return mm.ToString("00", CultureInfo.InvariantCulture) + ":"
                + ss.ToString("00", CultureInfo.InvariantCulture);
        }

        public static bool HasCoverageGap(IList<EyeCareSegment> list)
        {
            if (list == null || list.Count == 0) return true;
            bool[] day = new bool[1440];
            for (int i = 0; i < list.Count; i++)
            {
                EyeCareSegment seg = list[i];
                if (seg == null || seg.Disabled) continue;
                int a, b;
                if (!TryParseHm(seg.StartHm, out a) || !TryParseHm(seg.EndHm, out b)) continue;
                MarkRange(day, a, b);
            }
            for (int i = 0; i < 1440; i++)
            {
                if (!day[i]) return true;
            }
            return false;
        }

        public static bool TryNextSegment(DateTime local, IList<EyeCareSegment> list, out EyeCareSegment next)
        {
            next = null;
            if (list == null) return false;
            int t = local.Hour * 60 + local.Minute;
            int best = int.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                EyeCareSegment seg = list[i];
                if (seg == null || seg.Disabled) continue;
                int s, e;
                if (!TryParseHm(seg.StartHm, out s) || !TryParseHm(seg.EndHm, out e)) continue;
                if (IsInSchedule(local, s, e)) continue;
                int wait = (s - t + 1440) % 1440;
                if (wait == 0) wait = 1440;
                if (wait < best)
                {
                    best = wait;
                    next = seg;
                }
            }
            return next != null;
        }

        public static bool TrySuggestNew(IList<EyeCareSegment> existing, out EyeCareSegment neu)
        {
            neu = MakeSegment("09:00", "13:00", EveningKelvin, DefaultBrightness);
            var trial = CloneSegments(existing);
            trial.Add(neu);
            if (ValidateSegments(trial) == null) return true;

            bool[] day = new bool[1440];
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    EyeCareSegment seg = existing[i];
                    if (seg == null || seg.Disabled) continue;
                    int a, b;
                    if (!TryParseHm(seg.StartHm, out a) || !TryParseHm(seg.EndHm, out b)) continue;
                    MarkRange(day, a, b);
                }
            }
            int run = 0;
            int runStart = 0;
            for (int i = 0; i < 1440; i++)
            {
                if (!day[i])
                {
                    if (run == 0) runStart = i;
                    run++;
                    if (run >= 60)
                    {
                        neu = MakeSegment(FormatHm(runStart), FormatHm(runStart + 60), EveningKelvin, DefaultBrightness);
                        return true;
                    }
                }
                else run = 0;
            }
            return false;
        }

        public static string MatchPresetName(string startHm, string endHm)
        {
            string a = NormalizeHm(startHm);
            string b = NormalizeHm(endHm);
            if (a == DayStart && b == DayEnd) return PresetDayName;
            if (a == EveningStart && b == EveningEnd) return PresetEveningName;
            if (a == NightStart && b == NightEnd) return PresetNightName;
            return PresetCustomName;
        }

        public static EyeCareSegment SegmentFromPreset(string name)
        {
            if (name == PresetDayName) return MakeSegment(DayStart, DayEnd, DayKelvin, DefaultBrightness);
            if (name == PresetEveningName) return MakeSegment(EveningStart, EveningEnd, EveningKelvin, DefaultBrightness);
            if (name == PresetNightName) return MakeSegment(NightStart, NightEnd, NightKelvin, DefaultBrightness);
            return null;
        }

        /// <summary>补齐或从旧单时段字段迁出。有改动返回 true。</summary>
        public static bool EnsureSegments(AppSettings settings)
        {
            return EnsureSegments(settings, true);
        }

        public static bool EnsureSegments(AppSettings settings, bool migrateLegacy)
        {
            if (settings == null) return false;
            if (settings.EyeCareSegments != null && settings.EyeCareSegments.Count > 0)
            {
                NormalizeSegmentList(settings.EyeCareSegments);
                if (ValidateSegments(settings.EyeCareSegments) == null)
                {
                    if (LooksLikeUnusedPlaceholder(settings))
                    {
                        settings.EyeCareSegments = CreateDefaultSegments();
                        SyncLegacyFields(settings);
                        return true;
                    }
                    return false;
                }
                settings.EyeCareSegments = CreateDefaultSegments();
                SyncLegacyFields(settings);
                return true;
            }

            if (!migrateLegacy)
            {
                settings.EyeCareSegments = CreateDefaultSegments();
                SyncLegacyFields(settings);
                return true;
            }

            int k = NormalizeKelvin(settings.EyeCareKelvin);
            int b = NormalizeBrightness(settings.EyeCareBrightness);
            if (settings.EyeCareSchedule)
            {
                string start = string.IsNullOrWhiteSpace(settings.EyeCareStartHm) ? DefaultStart : settings.EyeCareStartHm;
                string end = string.IsNullOrWhiteSpace(settings.EyeCareEndHm) ? DefaultEnd : settings.EyeCareEndHm;
                settings.EyeCareSegments = new List<EyeCareSegment> { MakeSegment(start, end, k, b) };
                if (ValidateSegments(settings.EyeCareSegments) != null)
                    settings.EyeCareSegments = CreateDefaultSegments();
            }
            else if (settings.EyeCareEnabled)
                settings.EyeCareSegments = new List<EyeCareSegment> { MakeSegment("00:00", "00:00", k, b) };
            else
                settings.EyeCareSegments = CreateDefaultSegments();

            SyncLegacyFields(settings);
            return true;
        }

        public static void SyncLegacyFields(AppSettings settings)
        {
            if (settings == null || settings.EyeCareSegments == null || settings.EyeCareSegments.Count == 0)
                return;
            EyeCareSegment first = settings.EyeCareSegments[0];
            settings.EyeCareStartHm = NormalizeHm(first.StartHm) ?? DefaultStart;
            settings.EyeCareEndHm = NormalizeHm(first.EndHm) ?? DefaultEnd;
            settings.EyeCareKelvin = NormalizeKelvin(first.Kelvin);
            settings.EyeCareBrightness = NormalizeBrightness(first.Brightness);
            settings.EyeCareSchedule = true;
        }

        public static void NormalizeSegmentList(IList<EyeCareSegment> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                EyeCareSegment seg = list[i];
                if (seg == null) continue;
                int a, b;
                if (TryParseHm(seg.StartHm, out a)) seg.StartHm = FormatHm(a);
                if (TryParseHm(seg.EndHm, out b)) seg.EndHm = FormatHm(b);
                if (seg.Kelvin > 0) seg.Kelvin = ClampKelvin(seg.Kelvin);
                else seg.Kelvin = DefaultKelvin;
                if (seg.Brightness > 0) seg.Brightness = ClampBrightness(seg.Brightness);
                else seg.Brightness = DefaultBrightness;
            }
        }

        private static bool LooksLikeUnusedPlaceholder(AppSettings settings)
        {
            if (settings == null || settings.EyeCareEnabled) return false;
            if (settings.EyeCareSegments == null || settings.EyeCareSegments.Count != 1) return false;
            EyeCareSegment seg = settings.EyeCareSegments[0];
            if (seg == null) return false;
            int a, b;
            if (!TryParseHm(seg.StartHm, out a) || !TryParseHm(seg.EndHm, out b)) return false;
            if (a != 0 || b != 0) return false;
            return NormalizeKelvin(seg.Kelvin) == DefaultKelvin
                && NormalizeBrightness(seg.Brightness) == DefaultBrightness;
        }

        private static string NormalizeHm(string hm)
        {
            int m;
            if (!TryParseHm(hm, out m)) return null;
            return FormatHm(m);
        }

        private static void MarkRange(bool[] day, int startMin, int endMin)
        {
            if (day == null || day.Length < 1440) return;
            if (startMin == endMin)
            {
                for (int i = 0; i < 1440; i++) day[i] = true;
                return;
            }
            if (startMin < endMin)
            {
                for (int i = startMin; i < endMin; i++) day[i] = true;
                return;
            }
            for (int i = startMin; i < 1440; i++) day[i] = true;
            for (int i = 0; i < endMin; i++) day[i] = true;
        }

        private static byte ToByte(double v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)Math.Round(v);
        }

        private static ushort ToRamp(int i, double scale)
        {
            if (scale < 0) scale = 0;
            double v = i * 256.0 * scale;
            if (v < 0) return 0;
            if (v > 65535) return 65535;
            return (ushort)Math.Round(v);
        }

        private static void EnsureIncreasing(ushort[] ramp)
        {
            if (ramp == null || ramp.Length < 256) return;
            for (int i = 1; i < 256; i++)
            {
                if (ramp[i] <= ramp[i - 1] && ramp[i - 1] < 65535)
                    ramp[i] = (ushort)(ramp[i - 1] + 1);
            }
        }

        /// <summary>进程名（可带 .exe）对应的常见护眼软件显示名；未收录则 null。</summary>
        public static string MatchKnownTool(string processName)
        {
            string n = NormalizeProcessName(processName);
            if (n == null) return null;
            string label;
            return KnownTools.TryGetValue(n, out label) ? label : null;
        }

        /// <summary>nightLightOn 为系统夜间模式（Win10+，尽力识别）。</summary>
        public static List<string> CollectConflictLabels(IList<string> processNames, bool nightLightOn)
        {
            var labels = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (processNames != null)
            {
                for (int i = 0; i < processNames.Count; i++)
                {
                    string label = MatchKnownTool(processNames[i]);
                    if (label == null || !seen.Add(label)) continue;
                    labels.Add(label);
                }
            }
            if (nightLightOn && seen.Add("系统夜间模式"))
                labels.Add("系统夜间模式");
            labels.Sort(StringComparer.OrdinalIgnoreCase);
            return labels;
        }

        /// <summary>无冲突返回 null，有则返回确认框正文。</summary>
        public static string BuildConflictPrompt(IList<string> labels)
        {
            if (labels == null || labels.Count == 0) return null;
            var sb = new StringBuilder();
            sb.AppendLine("检测到本机可能已有其它护眼/色温工具在运行：");
            for (int i = 0; i < labels.Count; i++)
                sb.AppendLine("· " + labels[i]);
            sb.AppendLine();
            sb.Append("同时开启可能导致色温互相覆盖、屏幕闪跳。仍要开启 IntraBox 护眼模式吗？");
            return sb.ToString();
        }

        /// <summary>Win10 夜间模式 CloudStore Data 的常见开灯标记（字节 18 为 0x15）。无法判断则 false。</summary>
        public static bool LooksLikeNightLightOn(byte[] data)
        {
            if (data == null || data.Length < 19) return false;
            return data[18] == 0x15;
        }

        private static string NormalizeProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return null;
            string n = processName.Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                n = n.Substring(0, n.Length - 4);
            if (n.Length == 0) return null;
            return n.ToLowerInvariant();
        }

        private static readonly Dictionary<string, string> KnownTools = CreateKnownTools();

        private static Dictionary<string, string> CreateKnownTools()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            d["flux"] = "f.lux";
            d["redshift"] = "Redshift";
            d["redshift-gtk"] = "Redshift";
            d["gtk-redshift"] = "Redshift";
            d["redshiftgui"] = "Redshift";
            d["careueyes"] = "CareUEyes";
            d["iris"] = "Iris";
            d["lightbulb"] = "LightBulb";
            d["sunsetscreen"] = "SunsetScreen";
            d["gammy"] = "Gammy";
            d["negativescreen"] = "NegativeScreen";
            d["pangobright"] = "PangoBright";
            d["eyeshield"] = "EyeShield";
            d["bluelightfilter"] = "BlueLightFilter";
            d["nightlight"] = "NightLight";
            d["circadian"] = "Circadian";
            return d;
        }
    }
}
