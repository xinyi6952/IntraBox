using System;

namespace IntraBox.Core
{
    /// <summary>
    /// 预览用 NavigateToString，文档地址是 about:blank。
    /// 点页内 #锚点会被当成导航，把正文冲成空白；外链也不该离开预览。
    /// </summary>
    public static class HtmlPreviewNavHelper
    {
        public const string ScriptMark = "ibxAnchorClick";

        /// <summary>IE8 可用的页内点击处理：#锚点滚动，其它链接只拦不跳。</summary>
        public const string InPageNavJs =
            "function ibxCancel(e){if(!e)return;if(e.preventDefault)e.preventDefault();e.returnValue=false;}"
            + "function ibxScroll(id){if(!id)return;try{id=decodeURIComponent(id);}catch(x){}"
            + "var el=document.getElementById(id);if(!el&&document.getElementsByName){var n=document.getElementsByName(id);if(n&&n.length)el=n[0];}"
            + "if(el&&el.scrollIntoView)el.scrollIntoView(true);}"
            + "function ibxAnchorClick(e){e=e||window.event;var t=e.target||e.srcElement;"
            + "while(t&&t.nodeName!=='A'&&t.nodeName!=='a')t=t.parentNode;if(!t)return true;"
            + "var href=t.getAttribute('href');if(href)href=String(href);"
            + "if(!href||href==='#' ){ibxCancel(e);return false;}"
            + "var low=href.toLowerCase();"
            + "if(low==='about:blank'){ibxCancel(e);return false;}"
            + "if(href.charAt(0)==='#'){ibxCancel(e);ibxScroll(href.substring(1));return false;}"
            + "var h=low.indexOf('#');"
            + "if(h>=0&&(low.indexOf('about:blank#')===0||low.indexOf('about:srcdoc#')===0))"
            + "{ibxCancel(e);ibxScroll(href.substring(h+1));return false;}"
            + "ibxCancel(e);return false;}"
            + "if(document.addEventListener)document.addEventListener('click',ibxAnchorClick,false);"
            + "else document.onclick=ibxAnchorClick;";

        public static string WithInPageNavScript(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;
            if (html.IndexOf(ScriptMark, StringComparison.Ordinal) >= 0) return html;
            string script = "<script type='text/javascript'>" + InPageNavJs + "</script>";
            int i = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return html.Insert(i, script);
            return html + script;
        }

        public static bool IsEngineLoadUri(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return true;
            if (StartsIgnore(uri, "data:")) return true;
            return EqualsIgnore(uri, "about:blank") || EqualsIgnore(uri, "about:srcdoc");
        }

        /// <summary>用户点空链接会导航到 about:blank，冲掉当前预览。</summary>
        public static bool ShouldCancelUserBlank(string uri, bool isUserInitiated)
        {
            if (!isUserInitiated) return false;
            return IsEngineLoadUri(uri) && !StartsIgnore(uri ?? "", "data:");
        }

        public static bool TryGetInPageFragment(string uri, out string id)
        {
            id = null;
            if (string.IsNullOrEmpty(uri)) return false;
            if (uri[0] == '#')
            {
                if (uri.Length <= 1) return false;
                id = DecodeFragment(uri.Substring(1));
                return !string.IsNullOrEmpty(id);
            }
            int hash = uri.IndexOf('#');
            if (hash < 0 || hash >= uri.Length - 1) return false;
            string baseUri = uri.Substring(0, hash);
            if (!EqualsIgnore(baseUri, "about:blank") && !EqualsIgnore(baseUri, "about:srcdoc"))
                return false;
            id = DecodeFragment(uri.Substring(hash + 1));
            return !string.IsNullOrEmpty(id);
        }

        public static bool IsVirtualFilesUri(string uri, string host)
        {
            if (string.IsNullOrEmpty(uri) || string.IsNullOrEmpty(host)) return false;
            return uri.IndexOf("://" + host, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool ShouldBlockExternal(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return false;
            if (StartsIgnore(uri, "http:") || StartsIgnore(uri, "https:")) return true;
            if (StartsIgnore(uri, "file:") || StartsIgnore(uri, "javascript:")) return true;
            if (StartsIgnore(uri, "mailto:")) return true;
            return false;
        }

        public static string DecodeFragment(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            try
            {
                return Uri.UnescapeDataString(raw);
            }
            catch
            {
                return raw;
            }
        }

        private static bool EqualsIgnore(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsIgnore(string s, string prefix)
        {
            return s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
