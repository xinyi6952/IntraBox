using System;
using System.Reflection;

namespace IntraBox.Core
{
    /// <summary>异常提示：面向用户展示时取最内层真正的原因，避免 TargetInvocationException 等包装异常吞掉真因。</summary>
    public static class ExceptionHelper
    {
        /// <summary>返回异常链最底层（GetBaseException）的 Message，用于界面提示。</summary>
        public static string RootMessage(this Exception ex)
        {
            if (ex == null) return "";
            var baseEx = ex.GetBaseException();
            return baseEx != null ? baseEx.Message : ex.Message;
        }

        /// <summary>
        /// OCR 弹窗文案：跳过 TIE/Aggregate 包装句，缺原生库时附 VC++ 说明，并保留底层详情。
        /// </summary>
        public static string FormatOcrFailure(Exception ex)
        {
            if (ex == null) return "";
            string detail = FirstNonWrapperMessage(ex);
            if (WinCompatHelper.LooksLikeMissingVcRuntime(ex))
            {
                string hint = WinCompatHelper.OcrVcRuntimeHint;
                if (string.IsNullOrEmpty(detail) || string.Equals(detail, hint, StringComparison.Ordinal))
                    return hint;
                return hint + Environment.NewLine + "详情：" + detail;
            }
            if (!string.IsNullOrEmpty(detail)) return detail;
            string fallback = ex.Message;
            if (string.IsNullOrWhiteSpace(fallback) || IsWrapperMessage(fallback))
                return ex.GetType().Name + ": " + (fallback ?? "");
            return fallback.Trim();
        }

        public static string FirstNonWrapperMessage(Exception ex)
        {
            for (Exception e = ex; e != null; e = NextCause(e))
            {
                if (IsWrapperType(e)) continue;
                string m = e.Message;
                if (string.IsNullOrWhiteSpace(m) || IsWrapperMessage(m)) continue;
                return m.Trim();
            }
            return "";
        }

        private static Exception NextCause(Exception e)
        {
            if (e == null) return null;
            var ae = e as AggregateException;
            if (ae != null)
            {
                if (ae.InnerException != null) return ae.InnerException;
                if (ae.InnerExceptions != null && ae.InnerExceptions.Count > 0)
                    return ae.InnerExceptions[0];
                return null;
            }
            return e.InnerException;
        }

        private static bool IsWrapperType(Exception e)
        {
            return e is TargetInvocationException || e is AggregateException;
        }

        private static bool IsWrapperMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return true;
            string t = message.Trim();
            if (t.StartsWith("调用的目标发生了异常", StringComparison.Ordinal)) return true;
            if (t.StartsWith("发生一个或多个错误", StringComparison.Ordinal)) return true;
            if (string.Equals(t, "One or more errors occurred.", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
