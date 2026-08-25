using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IntraBox.Core
{
    /// <summary>基础 JSON Schema：type / required / properties / items，以及从样例反推。</summary>
    public static class JsonSchemaUtil
    {
        public static string Infer(JToken token)
        {
            return BuildSchema(token).ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static JObject BuildSchema(JToken token)
        {
            var obj = new JObject();
            if (token == null || token.Type == JTokenType.Null)
            {
                obj["type"] = "null";
                return obj;
            }
            switch (token.Type)
            {
                case JTokenType.Object:
                    obj["type"] = "object";
                    var props = new JObject();
                    var req = new JArray();
                    foreach (var p in ((JObject)token).Properties())
                    {
                        props[p.Name] = BuildSchema(p.Value);
                        req.Add(p.Name);
                    }
                    obj["properties"] = props;
                    if (req.Count > 0) obj["required"] = req;
                    break;
                case JTokenType.Array:
                    obj["type"] = "array";
                    var arr = (JArray)token;
                    if (arr.Count > 0) obj["items"] = BuildSchema(arr[0]);
                    break;
                case JTokenType.Integer:
                    obj["type"] = "integer";
                    break;
                case JTokenType.Float:
                    obj["type"] = "number";
                    break;
                case JTokenType.Boolean:
                    obj["type"] = "boolean";
                    break;
                default:
                    obj["type"] = "string";
                    break;
            }
            return obj;
        }

        public static List<string> Validate(JToken data, JToken schema, string path)
        {
            var errors = new List<string>();
            if (schema == null || schema.Type != JTokenType.Object)
            {
                errors.Add("Schema 不是对象");
                return errors;
            }
            ValidateNode(data, (JObject)schema, string.IsNullOrEmpty(path) ? "$" : path, errors);
            return errors;
        }

        private static void ValidateNode(JToken data, JObject schema, string path, List<string> errors)
        {
            var typeTok = schema["type"];
            if (typeTok != null && typeTok.Type == JTokenType.String)
            {
                string t = typeTok.Value<string>();
                if (!MatchType(data, t))
                    errors.Add(path + " 期望类型 " + t + "，实际是 " + data.Type);
            }
            if (data != null && data.Type == JTokenType.Object && schema["properties"] is JObject props)
            {
                var obj = (JObject)data;
                if (schema["required"] is JArray req)
                {
                    foreach (var r in req)
                    {
                        string name = r.Value<string>();
                        if (obj.Property(name) == null)
                            errors.Add(path + " 缺少必填字段 " + name);
                    }
                }
                foreach (var p in props.Properties())
                {
                    if (obj.Property(p.Name) == null) continue;
                    if (p.Value is JObject child)
                        ValidateNode(obj[p.Name], child, path + "." + p.Name, errors);
                }
            }
            if (data != null && data.Type == JTokenType.Array && schema["items"] is JObject items)
            {
                int i = 0;
                foreach (var el in (JArray)data)
                {
                    ValidateNode(el, items, path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", errors);
                    i++;
                }
            }
        }

        private static bool MatchType(JToken data, string type)
        {
            if (data == null) return type == "null";
            switch (type)
            {
                case "object": return data.Type == JTokenType.Object;
                case "array": return data.Type == JTokenType.Array;
                case "string": return data.Type == JTokenType.String;
                case "integer": return data.Type == JTokenType.Integer;
                case "number": return data.Type == JTokenType.Integer || data.Type == JTokenType.Float;
                case "boolean": return data.Type == JTokenType.Boolean;
                case "null": return data.Type == JTokenType.Null;
                default: return true;
            }
        }

        public static string FormatErrors(List<string> errors)
        {
            if (errors == null || errors.Count == 0) return "校验通过";
            var sb = new StringBuilder();
            foreach (var e in errors) sb.AppendLine(e);
            return sb.ToString().TrimEnd();
        }
    }
}
