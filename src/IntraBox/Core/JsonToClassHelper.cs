using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IntraBox.Core
{
    /// <summary>JSON 转 C#/Java 模型：标识符清洗与类型推断。纯逻辑，便于单元测试。</summary>
    public static class JsonToClassHelper
    {
        public static string SanitizeIdent(string raw, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var sb = new StringBuilder();
            foreach (var c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
            }
            if (sb.Length == 0) return fallback;
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }
    }

    /// <summary>根据 JToken 推断类型并输出一个或多个类定义。</summary>
    public sealed class ClassEmitter
    {
        private readonly bool _java;
        private readonly Dictionary<string, string> _classes = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

        public ClassEmitter(bool java)
        {
            _java = java;
        }

        public string Emit(JToken token, string rootName)
        {
            if (token is JArray)
            {
                var arr = (JArray)token;
                var item = MergeArray(arr);
                var wrapper = new JObject();
                var items = new JArray();
                if (item != null && item.Type != JTokenType.Null)
                    items.Add(item);
                wrapper["Items"] = items;
                BuildClass(rootName, wrapper);
            }
            else if (token is JObject)
            {
                BuildClass(rootName, (JObject)token);
            }
            else
            {
                var wrapper = new JObject();
                wrapper["Value"] = token;
                BuildClass(rootName, wrapper);
            }

            var sb = new StringBuilder();
            if (_java)
            {
                sb.AppendLine("// JSON 生成的 Java 模型（public 字段）");
                sb.AppendLine("import java.util.Date;");
                sb.AppendLine("import java.util.List;");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("// JSON 生成的 C# 模型");
                sb.AppendLine("using System;");
                sb.AppendLine("using System.Collections.Generic;");
                sb.AppendLine();
            }
            foreach (var kv in _classes)
            {
                sb.AppendLine(kv.Value);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private void BuildClass(string className, JObject obj)
        {
            className = JsonToClassHelper.SanitizeIdent(className, "Model");
            if (className.Length > 0)
                className = char.ToUpperInvariant(className[0]) + (className.Length > 1 ? className.Substring(1) : "");
            if (_classes.ContainsKey(className)) return;
            _classes[className] = "";
            _used.Add(className);
            var body = new StringBuilder();
            body.AppendLine("public class " + className);
            body.AppendLine("{");

            foreach (var prop in obj.Properties())
            {
                var field = ToPascal(prop.Name);
                if (field.Length == 0) field = "Field";
                var typeName = ResolveType(prop.Value, field);
                if (_java)
                    body.AppendLine("    public " + ToJavaType(typeName) + " " + ToCamel(field) + ";");
                else
                    body.AppendLine("    public " + typeName + " " + UniqueField(field) + " { get; set; }");
            }

            body.Append("}");
            _classes[className] = body.ToString();
        }

        private string ResolveType(JToken token, string hint)
        {
            if (token == null || token.Type == JTokenType.Null)
                return _java ? "Object" : "object";
            switch (token.Type)
            {
                case JTokenType.Boolean:
                    return _java ? "boolean" : "bool";
                case JTokenType.Integer:
                    var l = token.Value<long>();
                    if (l >= int.MinValue && l <= int.MaxValue)
                        return _java ? "int" : "int";
                    return _java ? "long" : "long";
                case JTokenType.Float:
                    return _java ? "double" : "double";
                case JTokenType.String:
                    return GuessStringType(token.Value<string>());
                case JTokenType.Array:
                    var merged = MergeArray((JArray)token);
                    var item = ResolveType(merged, hint);
                    return _java ? "List<" + BoxJava(item) + ">" : "List<" + BoxCs(item) + ">";
                case JTokenType.Object:
                    var name = ToPascal(hint);
                    BuildClass(name, (JObject)token);
                    return name;
                default:
                    return _java ? "Object" : "object";
            }
        }

        private static string GuessStringType(string s)
        {
            DateTime dt;
            if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt)
                && (s.IndexOf('-') > 0 || s.IndexOf(':') > 0))
                return "DateTime";
            return "string";
        }

        private static JToken MergeArray(JArray arr)
        {
            JObject merged = null;
            JToken lastScalar = null;
            foreach (var item in arr)
            {
                if (item is JObject)
                {
                    if (merged == null) merged = new JObject();
                    foreach (var p in ((JObject)item).Properties())
                    {
                        if (merged[p.Name] == null || merged[p.Name].Type == JTokenType.Null)
                            merged[p.Name] = p.Value;
                    }
                }
                else if (item.Type != JTokenType.Null)
                {
                    lastScalar = item;
                }
            }
            if (merged != null) return merged;
            if (lastScalar != null) return lastScalar;
            return JValue.CreateNull();
        }

        private static string UniqueField(string name)
        {
            if (name == "class" || name == "event" || name == "namespace" || name == "object")
                return name + "_";
            return name;
        }

        private static string ToPascal(string name)
        {
            var words = NameCaseHelper.SplitWords(name);
            if (words.Count == 0) return JsonToClassHelper.SanitizeIdent(name, "Field");
            var sb = new StringBuilder();
            foreach (var w in words)
            {
                var clean = JsonToClassHelper.SanitizeIdent(w, "");
                if (clean.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(clean[0]));
                if (clean.Length > 1) sb.Append(clean.Substring(1));
            }
            return sb.Length == 0 ? "Field" : sb.ToString();
        }

        private static string ToCamel(string pascal)
        {
            if (string.IsNullOrEmpty(pascal)) return "field";
            if (pascal.Length == 1) return pascal.ToLowerInvariant();
            return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        }

        private static string ToJavaType(string csOrJava)
        {
            if (csOrJava == "string") return "String";
            if (csOrJava == "DateTime") return "Date";
            if (csOrJava == "object") return "Object";
            if (csOrJava == "bool") return "boolean";
            return csOrJava;
        }

        private static string BoxCs(string t)
        {
            if (t == "int") return "int";
            if (t == "long") return "long";
            if (t == "double") return "double";
            if (t == "bool") return "bool";
            return t;
        }

        private static string BoxJava(string t)
        {
            if (t == "int") return "Integer";
            if (t == "long") return "Long";
            if (t == "double") return "Double";
            if (t == "boolean" || t == "bool") return "Boolean";
            if (t == "string") return "String";
            if (t == "DateTime") return "Date";
            return t;
        }
    }
}
