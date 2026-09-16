using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;

namespace IntraBox.Core
{
    /// <summary>轻量 WSDL 解析：列操作并生成 SOAP 信封模板。复杂多绑定/巨型 schema 失败并给中文说明。</summary>
    public static class JobWsdlHelper
    {
        public const int MaxWsdlChars = 2 * 1024 * 1024;
        public const int MaxOperations = 200;

        public sealed class WsdlOperation
        {
            public string Name { get; set; }
            public string SoapAction { get; set; }
            public string SoapVersion { get; set; }
            public string Endpoint { get; set; }
            public string Envelope { get; set; }
            public string XmlNamespaces { get; set; }
            public string Hint { get; set; }
        }

        public sealed class WsdlImportResult
        {
            public bool Ok { get; set; }
            public string Error { get; set; }
            public string Endpoint { get; set; }
            public string SoapVersion { get; set; }
            public string Warning { get; set; }
            public List<WsdlOperation> Operations { get; set; }
        }

        public static bool TryLoadText(string urlOrPath, int timeoutMs, out string xml, out string error)
        {
            xml = null;
            error = null;
            if (string.IsNullOrWhiteSpace(urlOrPath))
            {
                error = "请填写 WSDL 地址或本地文件路径";
                return false;
            }
            string src = urlOrPath.Trim();
            try
            {
                if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    WinCompatHelper.EnableModernTls();
                    var req = (HttpWebRequest)WebRequest.Create(src);
                    req.Method = "GET";
                    req.Timeout = timeoutMs > 0 ? timeoutMs : 30000;
                    req.ReadWriteTimeout = req.Timeout;
                    req.UserAgent = "IntraBox-JobFlow/1.0";
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var stream = resp.GetResponseStream())
                    using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                    {
                        xml = reader.ReadToEnd();
                    }
                }
                else
                {
                    if (!File.Exists(src))
                    {
                        error = "找不到 WSDL 文件：" + src;
                        return false;
                    }
                    var fi = new FileInfo(src);
                    if (fi.Length > MaxWsdlChars)
                    {
                        error = "WSDL 文件过大（超过约 2MB），请用更简单的定义或手工填 SOAP";
                        return false;
                    }
                    xml = File.ReadAllText(src, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                error = "读取 WSDL 失败：" + ex.RootMessage();
                return false;
            }

            if (string.IsNullOrWhiteSpace(xml))
            {
                error = "WSDL 内容为空";
                return false;
            }
            if (xml.Length > MaxWsdlChars)
            {
                error = "WSDL 内容过大（超过约 2MB），不支持完整导入";
                xml = null;
                return false;
            }
            return true;
        }

        public static WsdlImportResult Parse(string wsdlXml)
        {
            var result = new WsdlImportResult { Operations = new List<WsdlOperation>() };
            if (string.IsNullOrWhiteSpace(wsdlXml))
            {
                result.Error = "WSDL 内容为空";
                return result;
            }
            XmlDocument doc;
            try
            {
                var settings = new XmlReaderSettings();
                settings.DtdProcessing = DtdProcessing.Prohibit;
                settings.XmlResolver = null;
                using (var sr = new StringReader(wsdlXml))
                using (var reader = XmlReader.Create(sr, settings))
                {
                    doc = new XmlDocument();
                    doc.XmlResolver = null;
                    doc.Load(reader);
                }
            }
            catch (Exception ex)
            {
                result.Error = "WSDL 不是合法 XML：" + ex.RootMessage();
                return result;
            }

            XmlNode defs = doc.DocumentElement;
            if (defs == null || (!LocalNameEquals(defs, "definitions") && !LocalNameEquals(defs, "Description")))
            {
                result.Error = "根节点不是 wsdl:definitions，无法导入";
                return result;
            }

            var soapAddresses = FindSoapAddresses(defs);
            if (soapAddresses.Count == 0)
            {
                result.Error = "未找到 soap:address / soap12:address。可能是无绑定服务、仅含抽象定义，或依赖外部 import；请手工填写端点与信封";
                return result;
            }
            if (soapAddresses.Count > 1)
            {
                result.Warning = "检测到多个 SOAP 地址，已取第一个；多绑定场景请核对端点";
            }
            result.Endpoint = soapAddresses[0].Location;
            result.SoapVersion = soapAddresses[0].Is12 ? "1.2" : "1.1";

            var bindings = SelectChildren(defs, "binding");
            if (bindings.Count == 0)
            {
                result.Error = "未找到 binding，无法列出操作";
                return result;
            }
            if (bindings.Count > 3)
            {
                result.Error = "binding 过多（" + bindings.Count + "），超出轻量导入能力。请选用单一 SOAP binding 的 WSDL，或手工配置步骤";
                return result;
            }

            XmlNode soapBinding = null;
            bool is12 = soapAddresses[0].Is12;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (HasSoapBinding(bindings[i], is12))
                {
                    soapBinding = bindings[i];
                    break;
                }
            }
            if (soapBinding == null)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    if (HasSoapBinding(bindings[i], false) || HasSoapBinding(bindings[i], true))
                    {
                        soapBinding = bindings[i];
                        is12 = HasSoapBinding(bindings[i], true);
                        break;
                    }
                }
            }
            if (soapBinding == null)
            {
                result.Error = "未找到 SOAP binding（可能是 HTTP GET/POST 绑定）。本工具仅支持 SOAP 1.1/1.2";
                return result;
            }
            result.SoapVersion = is12 ? "1.2" : "1.1";

            var messages = IndexByName(SelectChildren(defs, "message"));
            var types = FindTypesSchema(defs);
            var ops = SelectChildren(soapBinding, "operation");
            if (ops.Count == 0)
            {
                result.Error = "SOAP binding 下没有 operation";
                return result;
            }
            if (ops.Count > MaxOperations)
            {
                result.Error = "操作数超过 " + MaxOperations + "，请缩小 WSDL 或手工配置";
                return result;
            }

            string tns = Attr(defs, "targetNamespace");
            var nsLines = new StringBuilder();
            if (!string.IsNullOrEmpty(tns))
                nsLines.AppendLine("tns=" + tns);
            nsLines.AppendLine("soap=" + (is12 ? "http://www.w3.org/2003/05/soap-envelope" : "http://schemas.xmlsoap.org/soap/envelope/"));

            for (int i = 0; i < ops.Count; i++)
            {
                XmlNode op = ops[i];
                string name = Attr(op, "name");
                if (string.IsNullOrEmpty(name)) continue;
                string action = FindSoapAction(op, is12);
                string envelope;
                string hint;
                BuildEnvelope(op, messages, types, tns, is12, out envelope, out hint);
                result.Operations.Add(new WsdlOperation
                {
                    Name = name,
                    SoapAction = action ?? "",
                    SoapVersion = result.SoapVersion,
                    Endpoint = result.Endpoint,
                    Envelope = envelope,
                    XmlNamespaces = nsLines.ToString().TrimEnd(),
                    Hint = hint
                });
            }

            if (result.Operations.Count == 0)
            {
                result.Error = "未能解析出任何操作";
                return result;
            }
            result.Ok = true;
            return result;
        }

        public static WsdlImportResult Import(string urlOrPath, int timeoutMs)
        {
            string xml, err;
            if (!TryLoadText(urlOrPath, timeoutMs, out xml, out err))
                return new WsdlImportResult { Error = err, Operations = new List<WsdlOperation>() };
            return Parse(xml);
        }

        private sealed class SoapAddr
        {
            public string Location;
            public bool Is12;
        }

        private static List<SoapAddr> FindSoapAddresses(XmlNode defs)
        {
            var list = new List<SoapAddr>();
            var services = SelectChildren(defs, "service");
            for (int s = 0; s < services.Count; s++)
            {
                var ports = SelectChildren(services[s], "port");
                for (int p = 0; p < ports.Count; p++)
                {
                    foreach (XmlNode child in ports[p].ChildNodes)
                    {
                        if (child.NodeType != XmlNodeType.Element) continue;
                        if (!LocalNameEquals(child, "address")) continue;
                        string loc = Attr(child, "location");
                        if (string.IsNullOrEmpty(loc)) continue;
                        bool is12 = NsContains(child.NamespaceURI, "soap12")
                            || child.NamespaceURI == "http://www.w3.org/2003/05/soap/bindings/HTTP/";
                        list.Add(new SoapAddr { Location = loc, Is12 = is12 });
                    }
                }
            }
            return list;
        }

        private static bool HasSoapBinding(XmlNode binding, bool want12)
        {
            foreach (XmlNode child in binding.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (!LocalNameEquals(child, "binding")) continue;
                string ns = child.NamespaceURI ?? "";
                if (want12)
                {
                    if (NsContains(ns, "soap12") || ns == "http://www.w3.org/2003/05/soap/bindings/HTTP/")
                        return true;
                }
                else
                {
                    if (NsContains(ns, "schemas.xmlsoap.org/wsdl/soap") || ns == "http://schemas.xmlsoap.org/wsdl/soap/")
                        return true;
                }
            }
            return false;
        }

        private static string FindSoapAction(XmlNode bindingOp, bool is12)
        {
            foreach (XmlNode child in bindingOp.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (!LocalNameEquals(child, "operation")) continue;
                string action = Attr(child, "soapAction");
                if (!string.IsNullOrEmpty(action)) return action;
            }
            return "";
        }

        private static void BuildEnvelope(
            XmlNode bindingOp,
            Dictionary<string, XmlNode> messages,
            XmlNode schema,
            string tns,
            bool is12,
            out string envelope,
            out string hint)
        {
            hint = null;
            string opName = Attr(bindingOp, "name") ?? "Operation";
            string bodyNs = is12 ? "http://www.w3.org/2003/05/soap-envelope" : "http://schemas.xmlsoap.org/soap/envelope/";
            string prefix = is12 ? "soap12" : "soap";

            // binding operation → input → body parts / 回退到同名 message
            XmlNode input = FirstChild(bindingOp, "input");
            List<string> partFields = new List<string>();
            string wrapperName = opName;
            string wrapperNs = tns ?? "";

            if (input != null)
            {
                string msgRef = Attr(input, "message");
                // 也可能在 portType；binding 常省略 message，靠同名 portType
                XmlNode msg = ResolveMessage(messages, msgRef);
                if (msg == null)
                    msg = FindMessageLoose(messages, opName);
                if (msg != null)
                {
                    var parts = SelectChildren(msg, "part");
                    for (int i = 0; i < parts.Count; i++)
                    {
                        string el = Attr(parts[i], "element");
                        string type = Attr(parts[i], "type");
                        string partName = Attr(parts[i], "name") ?? ("part" + (i + 1));
                        if (!string.IsNullOrEmpty(el))
                        {
                            string local;
                            string ns;
                            SplitQName(el, tns, out local, out ns);
                            wrapperName = local;
                            wrapperNs = ns;
                            CollectElementFields(schema, local, ns, partFields);
                        }
                        else if (!string.IsNullOrEmpty(type))
                        {
                            partFields.Add(partName);
                        }
                        else
                        {
                            partFields.Add(partName);
                        }
                    }
                }
            }

            if (partFields.Count == 0)
            {
                partFields.Add("arg0");
                hint = "未能从 schema 推断字段，已放入 ${arg0} 占位，请按实际改信封";
            }

            var sb = new StringBuilder();
            sb.Append('<').Append(prefix).Append(":Envelope xmlns:").Append(prefix).Append("=\"").Append(bodyNs).Append('"');
            if (!string.IsNullOrEmpty(wrapperNs))
                sb.Append(" xmlns:tns=\"").Append(EscapeAttr(wrapperNs)).Append('"');
            sb.Append('>');
            sb.Append('<').Append(prefix).Append(":Body>");
            string tag = string.IsNullOrEmpty(wrapperNs) ? wrapperName : ("tns:" + wrapperName);
            sb.Append('<').Append(tag).Append('>');
            for (int i = 0; i < partFields.Count; i++)
            {
                string f = SanitizeName(partFields[i]);
                sb.Append('<').Append(f).Append(">${").Append(f).Append("}</").Append(f).Append('>');
            }
            sb.Append("</").Append(tag).Append('>');
            sb.Append("</").Append(prefix).Append(":Body>");
            sb.Append("</").Append(prefix).Append(":Envelope>");
            envelope = sb.ToString();
        }

        private static void CollectElementFields(XmlNode schema, string elementLocal, string elementNs, List<string> fields)
        {
            if (schema == null || string.IsNullOrEmpty(elementLocal)) return;
            XmlNode el = FindSchemaElement(schema, elementLocal);
            if (el == null) return;
            XmlNode complex = FirstDescendant(el, "complexType");
            if (complex == null) complex = el;
            XmlNode seq = FirstDescendant(complex, "sequence");
            if (seq == null) seq = FirstDescendant(complex, "all");
            if (seq == null) seq = FirstDescendant(complex, "choice");
            if (seq == null) return;
            int count = 0;
            foreach (XmlNode n in seq.ChildNodes)
            {
                if (n.NodeType != XmlNodeType.Element || !LocalNameEquals(n, "element")) continue;
                string name = Attr(n, "name");
                if (string.IsNullOrEmpty(name)) continue;
                fields.Add(name);
                count++;
                if (count >= 20) break;
            }
        }

        private static XmlNode FindSchemaElement(XmlNode schema, string localName)
        {
            return FindNamedElement(schema, localName);
        }

        private static XmlNode FindNamedElement(XmlNode root, string localName)
        {
            if (root == null) return null;
            foreach (XmlNode n in root.ChildNodes)
            {
                if (n.NodeType != XmlNodeType.Element) continue;
                if (LocalNameEquals(n, "element") && string.Equals(Attr(n, "name"), localName, StringComparison.Ordinal))
                    return n;
                var hit = FindNamedElement(n, localName);
                if (hit != null) return hit;
            }
            return null;
        }

        private static XmlNode FindTypesSchema(XmlNode defs)
        {
            var types = FirstChild(defs, "types");
            if (types == null) return null;
            foreach (XmlNode n in types.ChildNodes)
            {
                if (n.NodeType == XmlNodeType.Element && LocalNameEquals(n, "schema"))
                    return n;
            }
            return null;
        }

        private static XmlNode ResolveMessage(Dictionary<string, XmlNode> map, string qname)
        {
            if (string.IsNullOrEmpty(qname) || map == null) return null;
            string local = qname;
            int p = qname.IndexOf(':');
            if (p >= 0) local = qname.Substring(p + 1);
            XmlNode n;
            if (map.TryGetValue(local, out n)) return n;
            return null;
        }

        private static XmlNode FindMessageLoose(Dictionary<string, XmlNode> map, string opName)
        {
            if (map == null) return null;
            string[] candidates = { opName, opName + "Request", opName + "In", "I" + opName + "_Request" };
            for (int i = 0; i < candidates.Length; i++)
            {
                XmlNode n;
                if (map.TryGetValue(candidates[i], out n)) return n;
            }
            foreach (var kv in map)
            {
                if (kv.Key.IndexOf(opName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Value;
            }
            return null;
        }

        private static Dictionary<string, XmlNode> IndexByName(List<XmlNode> nodes)
        {
            var map = new Dictionary<string, XmlNode>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                string name = Attr(nodes[i], "name");
                if (!string.IsNullOrEmpty(name) && !map.ContainsKey(name))
                    map[name] = nodes[i];
            }
            return map;
        }

        private static List<XmlNode> SelectChildren(XmlNode parent, string localName)
        {
            var list = new List<XmlNode>();
            if (parent == null) return list;
            foreach (XmlNode n in parent.ChildNodes)
            {
                if (n.NodeType == XmlNodeType.Element && LocalNameEquals(n, localName))
                    list.Add(n);
            }
            return list;
        }

        private static XmlNode FirstChild(XmlNode parent, string localName)
        {
            var list = SelectChildren(parent, localName);
            return list.Count > 0 ? list[0] : null;
        }

        private static XmlNode FirstDescendant(XmlNode parent, string localName)
        {
            if (parent == null) return null;
            foreach (XmlNode n in parent.ChildNodes)
            {
                if (n.NodeType != XmlNodeType.Element) continue;
                if (LocalNameEquals(n, localName)) return n;
                var d = FirstDescendant(n, localName);
                if (d != null) return d;
            }
            return null;
        }

        private static bool LocalNameEquals(XmlNode n, string local)
        {
            return n != null && string.Equals(n.LocalName, local, StringComparison.Ordinal);
        }

        private static bool NsContains(string ns, string fragment)
        {
            return !string.IsNullOrEmpty(ns) && ns.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Attr(XmlNode n, string name)
        {
            if (n == null || n.Attributes == null) return null;
            var a = n.Attributes[name];
            return a != null ? a.Value : null;
        }

        private static void SplitQName(string qname, string defaultNs, out string local, out string ns)
        {
            local = qname;
            ns = defaultNs ?? "";
            int p = qname.IndexOf(':');
            if (p > 0)
            {
                local = qname.Substring(p + 1);
                // 前缀对应 ns 在轻量解析里近似用 targetNamespace
            }
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "arg";
            var sb = new StringBuilder();
            string s = name.Trim();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            if (sb.Length == 0 || !(char.IsLetter(sb[0]) || sb[0] == '_'))
                sb.Insert(0, 'a');
            return sb.ToString();
        }

        private static string EscapeAttr(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");
        }

    }
}
