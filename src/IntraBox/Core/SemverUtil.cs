using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace IntraBox.Core
{
    /// <summary>语义化版本比较（MAJOR.MINOR.PATCH[-prerelease][+build]）与 ^ / ~ 范围。</summary>
    public static class SemverUtil
    {
        private static readonly Regex Rx = new Regex(
            @"^\s*v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z\.-]+))?(?:\+([0-9A-Za-z\.-]+))?\s*$",
            RegexOptions.CultureInvariant);

        public sealed class Version
        {
            public int Major, Minor, Patch;
            public string Pre;
        }

        public static bool TryParse(string text, out Version ver, out string error)
        {
            ver = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "版本号为空";
                return false;
            }
            var m = Rx.Match(text.Trim());
            if (!m.Success)
            {
                error = "不是合法 SemVer（需 MAJOR.MINOR.PATCH，可选 -pre）";
                return false;
            }
            ver = new Version
            {
                Major = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Minor = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                Patch = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                Pre = m.Groups[4].Success ? m.Groups[4].Value : null
            };
            return true;
        }

        public static int Compare(Version a, Version b)
        {
            if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
            if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);
            if (a.Patch != b.Patch) return a.Patch.CompareTo(b.Patch);
            bool aPre = !string.IsNullOrEmpty(a.Pre);
            bool bPre = !string.IsNullOrEmpty(b.Pre);
            if (aPre && !bPre) return -1;
            if (!aPre && bPre) return 1;
            if (!aPre) return 0;
            return string.Compare(a.Pre, b.Pre, StringComparison.Ordinal);
        }

        public static bool CompareOp(Version a, Version b, string op)
        {
            int c = Compare(a, b);
            switch (op)
            {
                case ">": return c > 0;
                case "<": return c < 0;
                case "=":
                case "==": return c == 0;
                case ">=": return c >= 0;
                case "<=": return c <= 0;
                default: return false;
            }
        }

        /// <summary>^1.2.3 → >=1.2.3 &lt;2.0.0；~1.2.3 → >=1.2.3 &lt;1.3.0</summary>
        public static bool Satisfies(Version ver, string range, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(range))
            {
                error = "范围为空";
                return false;
            }
            range = range.Trim();
            char kind = range[0];
            string rest = range;
            if (kind == '^' || kind == '~') rest = range.Substring(1).Trim();
            Version baseV;
            if (!TryParse(rest, out baseV, out error)) return false;

            if (kind == '^')
            {
                var hi = new Version { Major = baseV.Major + 1, Minor = 0, Patch = 0 };
                return Compare(ver, baseV) >= 0 && Compare(ver, hi) < 0;
            }
            if (kind == '~')
            {
                var hi = new Version { Major = baseV.Major, Minor = baseV.Minor + 1, Patch = 0 };
                return Compare(ver, baseV) >= 0 && Compare(ver, hi) < 0;
            }
            return Compare(ver, baseV) == 0;
        }
    }
}
