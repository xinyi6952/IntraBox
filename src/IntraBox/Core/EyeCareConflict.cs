using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace IntraBox.Core
{
    /// <summary>探测本机是否已有同类色温/护眼程序。失败当没有，不抛。</summary>
    public static class EyeCareConflict
    {
        public static List<string> DetectRunning()
        {
            try
            {
                return EyeCareHelper.CollectConflictLabels(ListProcessNames(), IsNightLightOn());
            }
            catch
            {
                return new List<string>();
            }
        }

        private static List<string> ListProcessNames()
        {
            var names = new List<string>();
            Process[] ps = null;
            try { ps = Process.GetProcesses(); }
            catch { return names; }
            if (ps == null) return names;
            for (int i = 0; i < ps.Length; i++)
            {
                Process p = ps[i];
                try
                {
                    string n = p.ProcessName;
                    if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
            return names;
        }

        /// <summary>Win10+ CloudStore；Win7 无此键视为未开。</summary>
        private static bool IsNightLightOn()
        {
            try
            {
                using (var acc = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount"))
                {
                    if (acc == null) return false;
                    string[] names = acc.GetSubKeyNames();
                    if (names == null) return false;
                    for (int i = 0; i < names.Length; i++)
                    {
                        string name = names[i];
                        if (name == null) continue;
                        if (name.IndexOf("bluelightreduction", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        using (var sub = acc.OpenSubKey(name))
                        {
                            if (ReadNightLightTree(sub, 0)) return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool ReadNightLightTree(RegistryKey key, int depth)
        {
            if (key == null || depth > 3) return false;
            var data = key.GetValue("Data") as byte[];
            if (EyeCareHelper.LooksLikeNightLightOn(data)) return true;
            string[] names;
            try { names = key.GetSubKeyNames(); }
            catch { return false; }
            if (names == null) return false;
            for (int i = 0; i < names.Length; i++)
            {
                using (var sub = key.OpenSubKey(names[i]))
                {
                    if (ReadNightLightTree(sub, depth + 1)) return true;
                }
            }
            return false;
        }
    }
}
