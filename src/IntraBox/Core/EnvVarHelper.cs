using System;
using System.Collections;
using System.Collections.Generic;

namespace IntraBox.Core
{
    /// <summary>
    /// 环境变量：列出进程 / 用户 / 系统；只允许改用户级。
    /// 空值写入用户级等于删除（与 Windows / .NET 行为一致）。
    /// </summary>
    public static class EnvVarHelper
    {
        public const int MaxValueChars = 32000;
        public const int PreviewChars = 80;

        public enum Scope
        {
            User = 0,
            Machine = 1,
            Process = 2
        }

        public sealed class Item
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Preview { get; set; }
            public Scope Scope { get; set; }
        }

        public static string ScopeText(Scope scope)
        {
            if (scope == Scope.User) return "用户";
            if (scope == Scope.Machine) return "系统";
            return "进程";
        }

        public static bool CanEdit(Scope scope)
        {
            return scope == Scope.User;
        }

        public static List<Item> List(Scope scope)
        {
            var list = new List<Item>();
            IDictionary table;
            try
            {
                table = Environment.GetEnvironmentVariables(ToTarget(scope));
            }
            catch
            {
                return list;
            }
            foreach (DictionaryEntry e in table)
            {
                string name = e.Key as string;
                if (string.IsNullOrEmpty(name)) continue;
                string value = e.Value as string ?? "";
                list.Add(new Item
                {
                    Name = name,
                    Value = value,
                    Preview = Preview(value),
                    Scope = scope
                });
            }
            list.Sort(CompareName);
            return list;
        }

        public static bool TryValidateName(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "名称不能为空";
                return false;
            }
            name = name.Trim();
            if (name.IndexOf('=') >= 0)
            {
                error = "名称不能包含 =";
                return false;
            }
            if (name.IndexOf('\0') >= 0)
            {
                error = "名称无效";
                return false;
            }
            return true;
        }

        public static bool TrySetUser(string name, string value, out string error)
        {
            if (!TryValidateName(name, out error)) return false;
            name = name.Trim();
            if (value != null && value.Length > MaxValueChars)
            {
                error = "值过长（最多 " + MaxValueChars + " 字符）";
                return false;
            }
            try
            {
                // null 或空字符串：删除该用户变量（.NET 对 User 目标的约定）
                string stored = string.IsNullOrEmpty(value) ? null : value;
                Environment.SetEnvironmentVariable(name, stored, EnvironmentVariableTarget.User);
                return true;
            }
            catch (Exception ex)
            {
                error = "写入失败：" + ex.RootMessage();
                return false;
            }
        }

        public static bool IsPathName(string name)
        {
            return string.Equals(name, "PATH", StringComparison.OrdinalIgnoreCase);
        }

        public static string Preview(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string one = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            if (one.Length <= PreviewChars) return one;
            return one.Substring(0, PreviewChars) + "…";
        }

        public static string[] SplitPath(string value)
        {
            if (string.IsNullOrEmpty(value)) return new string[0];
            var parts = value.Split(';');
            var list = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = (parts[i] ?? "").Trim();
                if (p.Length == 0) continue;
                list.Add(p);
            }
            return list.ToArray();
        }

        public static string JoinPath(IList<string> parts)
        {
            if (parts == null || parts.Count == 0) return "";
            var list = new List<string>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                string p = (parts[i] ?? "").Trim();
                if (p.Length == 0) continue;
                list.Add(p);
            }
            return string.Join(";", list.ToArray());
        }

        public static bool TryMove(IList<string> parts, int index, int delta)
        {
            if (parts == null || index < 0 || index >= parts.Count) return false;
            int n = index + delta;
            if (n < 0 || n >= parts.Count) return false;
            string t = parts[index];
            parts[index] = parts[n];
            parts[n] = t;
            return true;
        }

        public static string FindInScope(IList<Item> items, string name)
        {
            if (items == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return items[i].Value;
            }
            return null;
        }

        public static string OtherScopeHint(string name, string userValue, string machineValue, string processValue)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var bits = new List<string>();
            if (userValue != null) bits.Add("用户级有定义");
            if (machineValue != null) bits.Add("系统级有定义");
            if (processValue != null) bits.Add("当前进程能看到");
            if (bits.Count == 0) return "";
            if (IsPathName(name) && userValue != null && machineValue != null)
                bits.Add("进程 PATH 一般是系统 + 用户拼接");
            return string.Join("；", bits.ToArray()) + "。已打开的程序（含 IntraBox）不会立刻看到用户级改动，新开的终端才生效。";
        }

        private static EnvironmentVariableTarget ToTarget(Scope scope)
        {
            if (scope == Scope.User) return EnvironmentVariableTarget.User;
            if (scope == Scope.Machine) return EnvironmentVariableTarget.Machine;
            return EnvironmentVariableTarget.Process;
        }

        private static int CompareName(Item a, Item b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
