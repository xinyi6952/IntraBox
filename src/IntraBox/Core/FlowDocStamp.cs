using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace IntraBox.Core
{
    /// <summary>富文本文档的格式指纹，用于脏检测与示例是否被改过格式。</summary>
    public static class FlowDocStamp
    {
        public static string From(FlowDocument doc)
        {
            var sb = new StringBuilder();
            if (doc != null)
            {
                foreach (var block in doc.Blocks)
                    AppendStamp(sb, block);
            }
            return sb.ToString();
        }

        public static FlowDocument Roundtrip(FlowDocument doc)
        {
            if (doc == null) return null;
            using (var ms = new MemoryStream())
            {
                XamlWriter.Save(doc, ms);
                ms.Position = 0;
                return XamlReader.Load(ms) as FlowDocument;
            }
        }

        private static void AppendStamp(StringBuilder sb, Block block)
        {
            var p = block as Paragraph;
            if (p != null)
            {
                sb.Append('P');
                sb.Append((int)p.TextAlignment);
                sb.Append('/');
                sb.Append(p.FontSize);
                foreach (var inline in p.Inlines)
                    AppendInlineStamp(sb, inline);
                sb.Append('\n');
                return;
            }
            var list = block as List;
            if (list != null)
            {
                sb.Append('L');
                sb.Append((int)list.MarkerStyle);
                foreach (var item in list.ListItems)
                {
                    foreach (var inner in item.Blocks)
                        AppendStamp(sb, inner);
                }
                return;
            }
            var sec = block as Section;
            if (sec != null)
            {
                foreach (var inner in sec.Blocks)
                    AppendStamp(sb, inner);
            }
        }

        private static void AppendInlineStamp(StringBuilder sb, Inline inline)
        {
            var run = inline as Run;
            if (run != null)
            {
                sb.Append(run.Text);
                sb.Append('#');
                sb.Append(run.FontWeight);
                sb.Append(run.FontStyle);
                if (run.TextDecorations != null && run.TextDecorations.Count > 0)
                    sb.Append('U');
                var fg = run.Foreground as SolidColorBrush;
                if (fg != null) sb.Append(fg.Color);
                var bg = run.Background as SolidColorBrush;
                if (bg != null)
                {
                    sb.Append('B');
                    sb.Append(bg.Color);
                }
                return;
            }
            var span = inline as Span;
            if (span != null)
            {
                foreach (var child in span.Inlines)
                    AppendInlineStamp(sb, child);
                return;
            }
            if (inline is LineBreak)
            {
                sb.Append('\n');
                return;
            }
            var ui = inline as InlineUIContainer;
            if (ui != null)
            {
                var img = ui.Child as Image;
                sb.Append("[img:");
                sb.Append(img != null ? img.Tag as string : "");
                sb.Append(']');
            }
        }
    }
}
