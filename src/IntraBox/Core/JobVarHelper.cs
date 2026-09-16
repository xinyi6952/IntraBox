using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Newtonsoft.Json.Linq;

namespace IntraBox.Core
{
    /// <summary>编排变量名、替换、JSONPath/XPath 抽取与日志脱敏。</summary>
    public static class JobVarHelper
    {
        private static readonly Regex NameRx = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsValidName(string name)
        {
            return !string.IsNullOrEmpty(name) && NameRx.IsMatch(name);
        }

        /// <summary>
        /// 程序/批处理/Python 退出码变量名。ASCII 合法标识用 {name}_exitCode；
        /// 其它（含中文）用 s + UTF-8 十六进制 + _exitCode，避免一律变下划线导致碰撞。
        /// </summary>
        public static string StepExitCodeVar(string stepName)
        {
            string name = string.IsNullOrWhiteSpace(stepName) ? "step" : stepName.Trim();
            if (IsValidName(name))
                return name + "_exitCode";
            byte[] bytes = Encoding.UTF8.GetBytes(name);
            var sb = new StringBuilder(2 + bytes.Length * 2 + 9);
            sb.Append('s');
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            sb.Append("_exitCode");
            return sb.ToString();
        }

        public static bool TryValidateName(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "变量名不能为空";
                return false;
            }
            name = name.Trim();
            if (!IsValidName(name))
            {
                error = "变量名须字母或下划线开头，只含字母数字下划线";
                return false;
            }
            return true;
        }

        /// <summary>替换 ${name}；缺变量时返回 false，error 指明名称。</summary>
        public static bool TrySubstitute(string template, IDictionary<string, string> vars, out string result, out string error)
        {
            result = template ?? "";
            error = null;
            if (string.IsNullOrEmpty(template)) return true;
            var sb = new StringBuilder(template.Length);
            int i = 0;
            while (i < template.Length)
            {
                if (template[i] == '$' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    int end = template.IndexOf('}', i + 2);
                    if (end > i + 2)
                    {
                        string name = template.Substring(i + 2, end - (i + 2));
                        string val;
                        if (vars == null || !vars.TryGetValue(name, out val) || val == null)
                        {
                            error = "未定义变量 " + name;
                            result = null;
                            return false;
                        }
                        sb.Append(val);
                        i = end + 1;
                        continue;
                    }
                }
                sb.Append(template[i]);
                i++;
            }
            result = sb.ToString();
            return true;
        }

        /// <summary>收集 ${name}；跳过 run.today / run.now 以及 skip 集合。保序去重。</summary>
        public static void CollectNames(string template, IList<string> into, ICollection<string> skip)
        {
            if (string.IsNullOrEmpty(template) || into == null) return;
            int i = 0;
            while (i < template.Length)
            {
                if (template[i] == '$' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    int end = template.IndexOf('}', i + 2);
                    if (end > i + 2)
                    {
                        string name = template.Substring(i + 2, end - (i + 2)).Trim();
                        i = end + 1;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name == "run.today" || name == "run.now") continue;
                        if (skip != null && skip.Contains(name)) continue;
                        bool exists = false;
                        for (int k = 0; k < into.Count; k++)
                        {
                            if (string.Equals(into[k], name, StringComparison.Ordinal))
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists) into.Add(name);
                        continue;
                    }
                }
                i++;
            }
        }

        public static void SeedSystemVars(IDictionary<string, string> vars, DateTime now)
        {
            if (vars == null) return;
            vars["run.today"] = now.ToString("yyyy-MM-dd");
            vars["run.now"] = now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public static bool TryExtractJson(string json, string path, bool allowEmpty, out string value, out string error)
        {
            value = null;
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "抽取路径不能为空";
                return false;
            }
            if (string.IsNullOrEmpty(json))
            {
                if (allowEmpty) { value = ""; return true; }
                error = "响应体为空，无法按 JSON 抽取";
                return false;
            }
            try
            {
                var token = JToken.Parse(json);
                var hit = token.SelectToken(path.Trim());
                if (hit == null || hit.Type == JTokenType.Null)
                {
                    if (allowEmpty) { value = ""; return true; }
                    error = "JSON 路径未命中：" + path;
                    return false;
                }
                if (hit.Type == JTokenType.String || hit.Type == JTokenType.Integer
                    || hit.Type == JTokenType.Float || hit.Type == JTokenType.Boolean
                    || hit.Type == JTokenType.Date)
                    value = hit.ToString();
                else
                    value = hit.ToString(Newtonsoft.Json.Formatting.None);
                if (string.IsNullOrEmpty(value) && !allowEmpty)
                {
                    error = "抽取结果为空：" + path;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "JSON 抽取失败：" + ex.RootMessage();
                return false;
            }
        }

        public static bool TryExtractXPath(string xml, string xpath, string nsMap, bool allowEmpty, out string value, out string error)
        {
            value = null;
            error = null;
            if (string.IsNullOrWhiteSpace(xpath))
            {
                error = "XPath 不能为空";
                return false;
            }
            if (string.IsNullOrEmpty(xml))
            {
                if (allowEmpty) { value = ""; return true; }
                error = "响应体为空，无法按 XPath 抽取";
                return false;
            }
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                var nsm = new XmlNamespaceManager(doc.NameTable);
                AddNamespaces(nsm, nsMap);
                var node = doc.SelectSingleNode(xpath.Trim(), nsm);
                if (node == null)
                {
                    if (allowEmpty) { value = ""; return true; }
                    error = "XPath 未命中：" + xpath;
                    return false;
                }
                value = node.Value ?? node.InnerText ?? "";
                if (string.IsNullOrEmpty(value) && !allowEmpty)
                {
                    error = "抽取结果为空：" + xpath;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "XPath 抽取失败：" + ex.RootMessage();
                return false;
            }
        }

        public static void AddNamespaces(XmlNamespaceManager nsm, string mapText)
        {
            if (nsm == null || string.IsNullOrWhiteSpace(mapText)) return;
            string[] lines = mapText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = (lines[i] ?? "").Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int p = line.IndexOf('=');
                if (p <= 0) continue;
                string prefix = line.Substring(0, p).Trim();
                string uri = line.Substring(p + 1).Trim();
                if (prefix.Length == 0 || uri.Length == 0) continue;
                try { nsm.AddNamespace(prefix, uri); }
                catch { /* 忽略坏行 */ }
            }
        }

        public static bool LooksLikeSoapFault(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return false;
            return xml.IndexOf("Fault", StringComparison.OrdinalIgnoreCase) >= 0
                && (xml.IndexOf("faultcode", StringComparison.OrdinalIgnoreCase) >= 0
                    || xml.IndexOf(":Fault", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static List<KeyValuePair<string, string>> ParseHeaders(string text)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = (lines[i] ?? "").Trim();
                if (line.Length == 0) continue;
                int p = line.IndexOf(':');
                if (p <= 0) continue;
                list.Add(new KeyValuePair<string, string>(line.Substring(0, p).Trim(), line.Substring(p + 1).Trim()));
            }
            return list;
        }

        public static string RedactText(string text, IList<string> secretValues)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string s = text;
            if (secretValues != null)
            {
                for (int i = 0; i < secretValues.Count; i++)
                {
                    string v = secretValues[i];
                    if (string.IsNullOrEmpty(v) || v.Length < 2) continue;
                    s = s.Replace(v, "***");
                }
            }
            return RedactHeaderValues(s);
        }

        public static string RedactHeaderValues(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var sb = new StringBuilder(text.Length);
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                string line = lines[i];
                int p = line.IndexOf(':');
                if (p > 0)
                {
                    string name = line.Substring(0, p).Trim();
                    if (IsSensitiveHeader(name))
                    {
                        sb.Append(name);
                        sb.Append(": ***");
                        continue;
                    }
                }
                sb.Append(line);
            }
            return sb.ToString();
        }

        public static bool IsSensitiveHeader(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.Trim();
            if (n.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.Equals("Cookie", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
            return text.Substring(0, max) + "…";
        }
    }
}
