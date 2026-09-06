using System;
using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace IntraBox.Controls
{
    /// <summary>
    /// AvalonEdit 自带语法色板按浅色背景设计（标题暗红、引用藏青）。
    /// 深色主题下把过暗前景提亮，切回浅色时还原。
    /// </summary>
    public static class HighlightingTheme
    {
        private static readonly Dictionary<HighlightingColor, HighlightingBrush> OrigFg =
            new Dictionary<HighlightingColor, HighlightingBrush>();

        public static void Apply(IHighlightingDefinition def, bool dark)
        {
            if (def == null) return;
            foreach (var color in def.NamedHighlightingColors)
                Adapt(color, dark);
            Walk(def.MainRuleSet, dark, new HashSet<HighlightingRuleSet>());
        }

        private static void Walk(HighlightingRuleSet set, bool dark, HashSet<HighlightingRuleSet> seen)
        {
            if (set == null || !seen.Add(set)) return;
            if (set.Rules != null)
            {
                for (int i = 0; i < set.Rules.Count; i++)
                    Adapt(set.Rules[i].Color, dark);
            }
            if (set.Spans == null) return;
            for (int i = 0; i < set.Spans.Count; i++)
            {
                var span = set.Spans[i];
                if (span == null) continue;
                Adapt(span.StartColor, dark);
                Adapt(span.SpanColor, dark);
                Adapt(span.EndColor, dark);
                Walk(span.RuleSet, dark, seen);
            }
        }

        private static void Adapt(HighlightingColor color, bool dark)
        {
            if (color == null) return;
            if (!OrigFg.ContainsKey(color))
                OrigFg[color] = color.Foreground;
            var orig = OrigFg[color];
            if (!dark)
            {
                color.Foreground = orig;
                return;
            }
            if (orig == null) return;
            Color? c = orig.GetColor(null);
            if (c == null) return;
            if (Luminance(c.Value) >= 0.42)
            {
                color.Foreground = orig;
                return;
            }
            color.Foreground = new SimpleHighlightingBrush(Lighten(c.Value));
        }

        private static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        private static Color Lighten(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double h = 0, s, l = (max + min) / 2.0;
            if (max == min)
            {
                s = 0;
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6.0;
            }
            if (s < 0.35) s = 0.48;
            l = 0.72;
            HslToRgb(h, s, l, out r, out g, out b);
            return Color.FromRgb(ToByte(r), ToByte(g), ToByte(b));
        }

        private static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
        {
            if (s <= 0)
            {
                r = g = b = l;
                return;
            }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3.0);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        private static byte ToByte(double v)
        {
            if (v < 0) return 0;
            if (v > 1) return 255;
            return (byte)Math.Round(v * 255.0);
        }
    }
}
