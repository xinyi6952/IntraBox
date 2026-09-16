using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IntraBox.Core
{
    /// <summary>步骤循环：重复 N 次或 foreach 变量（JSON 数组 / 分号分隔）。非脚本引擎。</summary>
    public static class JobLoopHelper
    {
        public const int MaxIterations = 50;

        public enum LoopKind
        {
            None = 0,
            Repeat = 1,
            Foreach = 2
        }

        /// <summary>解析 foreach 源：JSON 数组（字符串或对象）或分号/换行分隔列表。</summary>
        public static bool TryParseItems(string sourceText, string itemProperty, out List<string> items, out string error)
        {
            items = new List<string>();
            error = null;
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                error = "循环源为空";
                return false;
            }
            string text = sourceText.Trim();
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    var arr = JArray.Parse(text);
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (items.Count >= MaxIterations)
                        {
                            error = "循环项超过上限 " + MaxIterations;
                            items = null;
                            return false;
                        }
                        string one;
                        if (!TryItemToString(arr[i], itemProperty, out one, out error))
                        {
                            items = null;
                            return false;
                        }
                        if (string.IsNullOrWhiteSpace(one)) continue;
                        items.Add(one);
                    }
                    if (items.Count == 0)
                    {
                        error = "循环源解析后无有效项";
                        items = null;
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    error = "循环源不是合法 JSON 数组：" + ex.RootMessage();
                    items = null;
                    return false;
                }
            }

            string[] parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split(new[] { ';', '\n' }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = (parts[i] ?? "").Trim();
                if (p.Length == 0) continue;
                if (items.Count >= MaxIterations)
                {
                    error = "循环项超过上限 " + MaxIterations;
                    items = null;
                    return false;
                }
                items.Add(p);
            }
            if (items.Count == 0)
            {
                error = "循环源解析后无有效项";
                items = null;
                return false;
            }
            return true;
        }

        public static bool TryResolveRepeatCount(int configured, out int count, out string error)
        {
            count = 0;
            error = null;
            if (configured < 1)
            {
                error = "重复次数至少为 1";
                return false;
            }
            if (configured > MaxIterations)
            {
                error = "重复次数不能超过 " + MaxIterations;
                return false;
            }
            count = configured;
            return true;
        }

        public static void ApplyItemVars(IDictionary<string, string> vars, string itemName, string itemValue, int index0)
        {
            if (vars == null) return;
            string name = string.IsNullOrWhiteSpace(itemName) ? "item" : itemName.Trim();
            vars[name] = itemValue ?? "";
            vars["loop.index"] = index0.ToString();
            vars["loop.count"] = (index0 + 1).ToString();
            vars[name + "_index"] = index0.ToString();
        }

        private static bool TryItemToString(JToken token, string itemProperty, out string value, out string error)
        {
            value = null;
            error = null;
            if (token == null || token.Type == JTokenType.Null)
            {
                value = "";
                return true;
            }
            if (token.Type == JTokenType.Object)
            {
                if (!string.IsNullOrWhiteSpace(itemProperty))
                {
                    var hit = token[itemProperty.Trim()];
                    if (hit == null || hit.Type == JTokenType.Null)
                    {
                        error = "循环项缺少属性：" + itemProperty;
                        return false;
                    }
                    value = hit.Type == JTokenType.String || hit.Type == JTokenType.Integer
                        || hit.Type == JTokenType.Float || hit.Type == JTokenType.Boolean
                        ? hit.ToString()
                        : hit.ToString(Newtonsoft.Json.Formatting.None);
                    return true;
                }
                value = token.ToString(Newtonsoft.Json.Formatting.None);
                return true;
            }
            if (token.Type == JTokenType.Array)
            {
                value = token.ToString(Newtonsoft.Json.Formatting.None);
                return true;
            }
            value = token.ToString();
            return true;
        }

        public static string KindText(LoopKind kind)
        {
            if (kind == LoopKind.Repeat) return "重复N次";
            if (kind == LoopKind.Foreach) return "遍历变量";
            return "无";
        }

        public static LoopKind ParseKind(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return LoopKind.None;
            string t = text.Trim();
            if (t == "重复N次" || t.Equals("Repeat", StringComparison.OrdinalIgnoreCase)) return LoopKind.Repeat;
            if (t == "遍历变量" || t.Equals("Foreach", StringComparison.OrdinalIgnoreCase)) return LoopKind.Foreach;
            return LoopKind.None;
        }
    }
}
