using System;
using System.IO;
using System.Text;
using IntraBox.Core;
using Markdig;

namespace IntraBox.Modules.Notes
{
    public static class NoteMarkdown
    {
        public const string SampleText =
            "# 【示例】排障手册（Markdown）\r\n\r\n" +
            "> 何时用 Markdown：步骤清单、命令/配置片段、要对照预览排版的长期手册。会议纪要、随手高亮和插图请看另一篇普通笔记示例。本篇可删。\r\n\r\n" +
            "## 怎么用这篇笔记\r\n\r\n" +
            "1. 顶部工具栏插入标题、列表、代码、待办、表格；**Ctrl+S** 保存（默认不自动保存，需要可勾选自动保存）。\r\n" +
            "2. 右上角切 **预览** 看排版，或 **分栏** 边改边看；**Ctrl+F** 查找。\r\n" +
            "3. 点右上角 × 关闭；有未保存改动会询问。\r\n\r\n" +
            "## 场景：支付回调超时（对照填写）\r\n\r\n" +
            "环境：测试 `pay-gateway`，出现时间 14:20。先排除本机 Hosts 指错环境，再用「文本比对」对比上周成功请求。\r\n\r\n" +
            "### 排查步骤\r\n\r\n" +
            "- [x] 看网关日志是否打到下游\r\n" +
            "- [ ] 核对本机 Hosts 是否指向测试环境\r\n" +
            "- [ ] 对比这次与上周成功请求的差异\r\n\r\n" +
            "超时配置：\r\n\r\n" +
            "```\r\n" +
            "timeout_ms=8000\r\n" +
            "retry=1\r\n" +
            "```\r\n\r\n" +
            "| 现象 | 先查 |\r\n" +
            "| --- | --- |\r\n" +
            "| 全部超时 | 下游存活、Hosts、防火墙 |\r\n" +
            "| 偶发超时 | 超时时间、重试、下游慢查询 |\r\n";

        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        public static string ToPreviewHtml(string markdown, string uid)
        {
            string md = markdown ?? "";
            string html = Markdown.ToHtml(md, Pipeline);
            html = RewriteLocalImages(html, uid);
            return BuildPage(html);
        }

        private static string RewriteLocalImages(string html, string uid)
        {
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(uid)) return html;
            string dir = DataPaths.NoteFilesDir(uid);
            if (!Directory.Exists(dir)) return html;
            return html.Replace("src=\"files/", "src=\"" + ToFileUri(dir) + "/")
                .Replace("src='files/", "src='" + ToFileUri(dir) + "/");
        }

        private static string ToFileUri(string dir)
        {
            try
            {
                return new Uri(Path.GetFullPath(dir)).AbsoluteUri.TrimEnd('/');
            }
            catch
            {
                return dir;
            }
        }

        private static string BuildPage(string bodyHtml)
        {
            return "<!DOCTYPE html><html><head><meta charset='utf-8'><meta http-equiv='X-UA-Compatible' content='IE=edge'/><style>"
                + "body{font-family:'Microsoft YaHei','Segoe UI',sans-serif;padding:14px;line-height:1.6;background:#1e1e1e;color:#ddd;}"
                + "h1,h2,h3{line-height:1.25;margin:0.7em 0 0.4em;} h1{border-bottom:1px solid #444;padding-bottom:4px;}"
                + "code{background:#111;padding:2px 5px;border-radius:3px;font-family:Consolas,monospace;}"
                + "pre{background:#111;padding:10px;border-radius:6px;overflow:auto;} pre code{padding:0;background:transparent;}"
                + "a{color:#6cb6ff;} blockquote{border-left:3px solid #555;margin:0;padding:0 12px;color:#aaa;}"
                + "table{border-collapse:collapse;margin:0.8em 0;} th,td{border:1px solid #555;padding:6px 12px;}"
                + "th{background:#333;font-weight:600;} del,s{color:#999;}"
                + "ul,ol{padding-left:1.8em;} img{max-width:100%;} hr{border:0;border-top:1px solid #444;}"
                + "</style></head><body>" + bodyHtml + "</body></html>";
        }
    }
}
