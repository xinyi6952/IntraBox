using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace IntraBox.Core
{
    /// <summary>编排 REST/SOAP 请求与 TCP 测通。仅 http/https。</summary>
    public static class JobHttpHelper
    {
        public static JobStepResult ProbeTcp(string url, int timeoutMs, CancellationToken token)
        {
            HttpProbeTarget t;
            string err;
            if (!HttpProbeHelper.TryParseUrl(url, out t, out err))
                return Fail(err);
            var sw = Stopwatch.StartNew();
            try
            {
                token.ThrowIfCancellationRequested();
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(t.Host, t.Port, null, null);
                    int wait = timeoutMs < 500 ? 500 : timeoutMs;
                    if (!ar.AsyncWaitHandle.WaitOne(wait))
                        return Fail("TCP 超时 " + t.Host + ":" + t.Port, sw.ElapsedMilliseconds);
                    client.EndConnect(ar);
                }
                sw.Stop();
                return new JobStepResult
                {
                    Ok = true,
                    Message = "TCP 通 " + t.Host + ":" + t.Port + "（" + sw.ElapsedMilliseconds + " ms）",
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Fail("TCP 失败：" + ex.RootMessage(), sw.ElapsedMilliseconds);
            }
        }

        public static JobStepResult SendRest(
            string method,
            string url,
            IList<KeyValuePair<string, string>> headers,
            string body,
            string contentTypeHint,
            int timeoutSec,
            bool insecure,
            CancellationToken token)
        {
            HttpProbeTarget t;
            string err;
            if (!HttpProbeHelper.TryParseUrl(url, out t, out err))
                return Fail(err);
            string m = string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant();
            return Send(m, url, headers, body, ResolveContentType(contentTypeHint, body), timeoutSec, insecure, token);
        }

        public static JobStepResult SendSoap(
            string url,
            string envelope,
            string soapAction,
            string soapVersion,
            int timeoutSec,
            bool insecure,
            CancellationToken token)
        {
            HttpProbeTarget t;
            string err;
            if (!HttpProbeHelper.TryParseUrl(url, out t, out err))
                return Fail(err);
            bool v12 = string.Equals(soapVersion, "1.2", StringComparison.OrdinalIgnoreCase);
            string ct = v12 ? "application/soap+xml; charset=utf-8" : "text/xml; charset=utf-8";
            var headers = new List<KeyValuePair<string, string>>();
            if (!v12 && !string.IsNullOrEmpty(soapAction))
                headers.Add(new KeyValuePair<string, string>("SOAPAction", "\"" + soapAction.Trim('"') + "\""));
            if (v12 && !string.IsNullOrEmpty(soapAction))
                ct = "application/soap+xml; charset=utf-8; action=\"" + soapAction.Trim('"') + "\"";
            return Send("POST", url, headers, envelope ?? "", ct, timeoutSec, insecure, token);
        }

        private static JobStepResult Send(
            string method,
            string url,
            IList<KeyValuePair<string, string>> headers,
            string body,
            string contentType,
            int timeoutSec,
            bool insecure,
            CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            bool oldExpect = ServicePointManager.Expect100Continue;
            SecurityProtocolType oldProto = ServicePointManager.SecurityProtocol;
            RemoteCertificateValidationCallback oldCb = ServicePointManager.ServerCertificateValidationCallback;
            try
            {
                token.ThrowIfCancellationRequested();
                WinCompatHelper.EnableModernTls();
                if (insecure)
                    ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true;

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                // Timeout/ReadWriteTimeout 单位毫秒；未填超时则为 Infinite
                int timeoutMs = JobLimits.ToHttpTimeoutMs(timeoutSec);
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.AllowAutoRedirect = true;
                req.KeepAlive = false;
                if (!string.IsNullOrEmpty(contentType) && NeedsBody(method))
                    req.ContentType = contentType;
                ApplyHeaders(req, headers);

                if (NeedsBody(method) && body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    req.ContentLength = bytes.Length;
                    using (var stream = req.GetRequestStream())
                    {
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }

                token.ThrowIfCancellationRequested();
                using (token.Register(delegate { try { req.Abort(); } catch { } }))
                {
                try
                {
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        bool truncated;
                        string text = ReadBody(resp, out truncated);
                        sw.Stop();
                        int code = (int)resp.StatusCode;
                        bool ok = code >= 200 && code < 300;
                        string msg = ok
                            ? ("HTTP " + code + "（" + sw.ElapsedMilliseconds + " ms）")
                            : ("HTTP " + code + "（" + sw.ElapsedMilliseconds + " ms）");
                        if (truncated)
                            msg += "；响应已按 " + JobLimits.FormatBytes(JobLimits.MaxResponseBytes) + " 截断";
                        return new JobStepResult
                        {
                            Ok = ok,
                            StatusCode = code,
                            Body = text,
                            ElapsedMs = sw.ElapsedMilliseconds,
                            Message = msg
                        };
                    }
                }
                catch (WebException wex)
                {
                    sw.Stop();
                    if (token.IsCancellationRequested)
                        throw new OperationCanceledException();
                    var resp = wex.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        bool truncated;
                        string text = ReadBody(resp, out truncated);
                        int code = (int)resp.StatusCode;
                        string msg = "HTTP " + code + "：" + TruncateMsg(wex.Message);
                        if (truncated)
                            msg += "；响应已截断";
                        return new JobStepResult
                        {
                            Ok = false,
                            StatusCode = code,
                            Body = text,
                            ElapsedMs = sw.ElapsedMilliseconds,
                            Message = msg
                        };
                    }
                    return Fail("请求失败：" + wex.RootMessage(), sw.ElapsedMilliseconds);
                }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Fail("请求异常：" + ex.RootMessage(), sw.ElapsedMilliseconds);
            }
            finally
            {
                ServicePointManager.Expect100Continue = oldExpect;
                ServicePointManager.SecurityProtocol = oldProto;
                ServicePointManager.ServerCertificateValidationCallback = oldCb;
            }
        }

        private static void ApplyHeaders(HttpWebRequest req, IList<KeyValuePair<string, string>> headers)
        {
            if (headers == null) return;
            for (int i = 0; i < headers.Count; i++)
            {
                string name = headers[i].Key;
                string val = headers[i].Value ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    req.ContentType = val;
                    continue;
                }
                if (name.Equals("Accept", StringComparison.OrdinalIgnoreCase))
                {
                    req.Accept = val;
                    continue;
                }
                if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    req.UserAgent = val;
                    continue;
                }
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                    continue;
                try { req.Headers[name] = val; }
                catch { /* 受限头跳过 */ }
            }
        }

        private static string ReadBody(HttpWebResponse resp, out bool truncated)
        {
            truncated = false;
            try
            {
                using (var stream = resp.GetResponseStream())
                {
                    if (stream == null) return "";
                    return JobLimits.ReadUtf8Limited(stream, JobLimits.MaxResponseBytes, out truncated);
                }
            }
            catch
            {
                return "";
            }
        }

        private static bool NeedsBody(string method)
        {
            return method != "GET" && method != "HEAD" && method != "DELETE";
        }

        private static string ResolveContentType(string hint, string body)
        {
            if (!string.IsNullOrWhiteSpace(hint))
            {
                string h = hint.Trim().ToLowerInvariant();
                if (h == "json" || h.Contains("json")) return "application/json; charset=utf-8";
                if (h == "form" || h.Contains("x-www-form")) return "application/x-www-form-urlencoded; charset=utf-8";
                if (h == "xml" || h.Contains("xml")) return "text/xml; charset=utf-8";
                if (h == "text" || h == "plain") return "text/plain; charset=utf-8";
                if (h.Contains("/")) return hint.Trim();
            }
            if (!string.IsNullOrEmpty(body) && body.TrimStart().StartsWith("{", StringComparison.Ordinal))
                return "application/json; charset=utf-8";
            return "text/plain; charset=utf-8";
        }

        private static JobStepResult Fail(string msg, long ms = 0)
        {
            return new JobStepResult { Ok = false, Message = msg, ElapsedMs = ms };
        }

        private static string TruncateMsg(string s)
        {
            return JobVarHelper.Truncate(s, 120);
        }
    }
}
