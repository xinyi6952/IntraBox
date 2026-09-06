using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IntraBox.Core;

namespace IntraBox.Modules.Todo
{
    public static class TodoRichText
    {
        public const string Template =
            "任务标题: \r\n任务来源:\r\n任务描述:\r\n";

        private const double ThumbMaxWidth = 200;
        private const double ThumbMaxHeight = 120;
        private const int ThumbDecodeWidth = 400;

        private static readonly DependencyProperty ItemUidProperty =
            DependencyProperty.RegisterAttached("ItemUid", typeof(string), typeof(TodoRichText), new PropertyMetadata(null));

        private static int _previewTick;

        public static string ExtractTitle(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var lines = plain.Replace("\r\n", "\n").Split('\n');
            const string prefix = "任务标题:";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                    return line.Substring(prefix.Length).Trim();
            }
            return "";
        }

        public static string ExtractField(string plain, string name)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var lines = plain.Replace("\r\n", "\n").Split('\n');
            string prefix = name + ":";
            var sb = new StringBuilder();
            bool grab = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string trimStart = raw.TrimStart();
                if (trimStart.StartsWith("任务标题:", StringComparison.Ordinal)
                    || trimStart.StartsWith("任务来源:", StringComparison.Ordinal)
                    || trimStart.StartsWith("任务描述:", StringComparison.Ordinal))
                {
                    if (trimStart.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        grab = true;
                        string rest = trimStart.Substring(prefix.Length).Trim();
                        if (rest.Length > 0) sb.AppendLine(rest);
                    }
                    else
                    {
                        grab = false;
                    }
                    continue;
                }
                if (grab) sb.AppendLine(raw);
            }
            return sb.ToString().Trim();
        }

        public static FlowDocument CreateTemplateDocument()
        {
            var doc = new FlowDocument();
            doc.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei, 微软雅黑, Segoe UI");
            doc.FontSize = 14;
            doc.Blocks.Add(new Paragraph(new Run("任务标题: ")));
            doc.Blocks.Add(new Paragraph(new Run("任务来源:")));
            doc.Blocks.Add(new Paragraph(new Run("任务描述:")));
            return doc;
        }

        public static FlowDocument CreateSampleDocument()
        {
            var doc = new FlowDocument();
            doc.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei, 微软雅黑, Segoe UI");
            doc.FontSize = 14;
            doc.Blocks.Add(Line("任务标题: 【示例】周五 18:00 前提交接口联调说明"));
            doc.Blocks.Add(Line("任务来源: 内网工单 WO-2026-0918（可取消「自动生成」后把工单号填到任务 ID）"));
            var desc = new Paragraph();
            desc.Margin = new Thickness(0, 0, 0, 6);
            desc.Inlines.Add(new Run("任务描述:"));
            desc.Inlines.Add(new LineBreak());
            desc.Inlines.Add(new Run("何时建任务：有截止日期、要周期性提醒、要对照工单的事项。随手备忘请用「笔记」，不要建任务。本条可删。"));
            desc.Inlines.Add(new LineBreak());
            desc.Inlines.Add(new LineBreak());
            desc.Inlines.Add(new Run("怎么用（对照本条）："));
            doc.Blocks.Add(desc);
            doc.Blocks.Add(Numbers(
                "标题写清交付物和截止；优先级选「高」，计划完成设到本周五 18:00。",
                "提醒选「仅工作日」15:00，IntraBox 在托盘时也会弹窗。",
                "描述里写步骤和验收；需要截图时点工具栏插图，或把 png/jpg 拖进来。",
                "做完后列表右键「标记为已办」。已办只能查看，改回待办后才能再改。"));
            var extra = new Paragraph();
            extra.Inlines.Add(new Run("验收：字段与错误码已对照接口文档；联调记录已放到共享目录。"));
            extra.Margin = new Thickness(0, 8, 0, 0);
            doc.Blocks.Add(extra);
            return doc;
        }

        private static Paragraph Line(string text)
        {
            var p = new Paragraph(new Run(text ?? ""));
            p.Margin = new Thickness(0, 0, 0, 6);
            return p;
        }

        private static System.Windows.Documents.List Numbers(params string[] items)
        {
            var list = new System.Windows.Documents.List();
            list.MarkerStyle = TextMarkerStyle.Decimal;
            list.Margin = new Thickness(0, 0, 0, 8);
            if (items == null) return list;
            for (int i = 0; i < items.Length; i++)
                list.ListItems.Add(new ListItem(new Paragraph(new Run(items[i]))));
            return list;
        }

        public static void SaveDocument(FlowDocument doc, string uid)
        {
            if (doc == null || string.IsNullOrEmpty(uid)) return;
            PrepareImagesForSave(doc);
            Directory.CreateDirectory(DataPaths.TodoItemDir(uid));
            using (var fs = File.Create(DataPaths.TodoDetailsPath(uid)))
            {
                XamlWriter.Save(doc, fs);
            }
        }

        public static string ToPlain(FlowDocument doc)
        {
            if (doc == null) return "";
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            return range.Text ?? "";
        }

        public static string ContentStamp(FlowDocument doc)
        {
            var sb = new StringBuilder();
            if (doc != null)
            {
                foreach (var block in doc.Blocks)
                    AppendStamp(sb, block);
            }
            return sb.ToString();
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
            var list = block as System.Windows.Documents.List;
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
                var brush = run.Foreground as SolidColorBrush;
                if (brush != null) sb.Append(brush.Color);
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

        public static void LoadInto(RichTextBox box, string uid)
        {
            var path = DataPaths.TodoDetailsPath(uid);
            if (box == null) return;
            if (!File.Exists(path))
            {
                box.Document = CreateTemplateDocument();
                return;
            }
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var doc = XamlReader.Load(fs) as FlowDocument;
                    if (doc == null)
                    {
                        box.Document = CreateTemplateDocument();
                        return;
                    }
                    RestoreImages(doc, uid);
                    box.Document = doc;
                }
            }
            catch
            {
                box.Document = CreateTemplateDocument();
            }
        }

        public static void SaveFrom(RichTextBox box, string uid)
        {
            if (box == null || string.IsNullOrEmpty(uid)) return;
            var doc = box.Document;
            SaveDocument(doc, uid);
            RestoreImages(doc, uid);
        }

        public static bool TryInsertImage(RichTextBox box, string uid, string srcFile, out string error)
        {
            error = null;
            if (box == null || string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(srcFile))
            {
                error = "无法插入图片";
                return false;
            }
            string ext = Path.GetExtension(srcFile);
            if (!IsAllowedImageExt(ext))
            {
                error = "仅支持 png / jpg / jpeg / gif / bmp";
                return false;
            }
            var info = new FileInfo(srcFile);
            if (!info.Exists)
            {
                error = "文件不存在";
                return false;
            }
            if (info.Length > SizeLimits.MaxFileBytes)
            {
                error = "图片超过设置中的文件大小上限";
                return false;
            }
            string filesDir = DataPaths.TodoFilesDir(uid);
            Directory.CreateDirectory(filesDir);
            string name = Guid.NewGuid().ToString("N") + ext.ToLowerInvariant();
            string dest = Path.Combine(filesDir, name);
            File.Copy(srcFile, dest, true);

            BitmapImage bmp;
            try
            {
                bmp = LoadBitmap(dest, ThumbDecodeWidth);
            }
            catch
            {
                error = "无法读取图片";
                return false;
            }

            var img = new Image();
            img.Tag = name;
            img.Uid = uid;
            img.Source = bmp;
            ApplyThumb(img, uid);
            var container = new InlineUIContainer(img);
            var caret = box.CaretPosition ?? box.Document.ContentEnd;
            caret.InsertTextInRun("");
            var para = caret.Paragraph;
            if (para == null)
            {
                para = new Paragraph();
                box.Document.Blocks.Add(para);
            }
            para.Inlines.Add(container);
            return true;
        }

        public static bool IsAllowedImageExt(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp";
        }

        private static void PrepareImagesForSave(FlowDocument doc)
        {
            foreach (var img in FindImages(doc))
            {
                img.Source = null;
            }
        }

        private static void RestoreImages(FlowDocument doc, string uid)
        {
            string dir = DataPaths.TodoFilesDir(uid);
            foreach (var img in FindImages(doc))
            {
                string name = img.Tag as string;
                if (string.IsNullOrEmpty(name)) continue;
                string path = Path.Combine(dir, name);
                if (!File.Exists(path)) continue;
                try
                {
                    img.Source = LoadBitmap(path, ThumbDecodeWidth);
                    ApplyThumb(img, uid);
                }
                catch { }
            }
        }

        private static void ApplyThumb(Image img, string uid)
        {
            if (img == null) return;
            if (!string.IsNullOrEmpty(uid))
                img.SetValue(ItemUidProperty, uid);
            img.Stretch = Stretch.Uniform;
            img.Cursor = Cursors.Hand;
            img.ToolTip = "点击查看大图";
            img.Focusable = false;
            img.IsHitTestVisible = true;
            img.SnapsToDevicePixels = true;
            var src = img.Source as BitmapSource;
            double pw = src != null && src.PixelWidth > 0 ? src.PixelWidth : ThumbMaxWidth;
            double ph = src != null && src.PixelHeight > 0 ? src.PixelHeight : ThumbMaxHeight;
            double scale = Math.Min(1.0, Math.Min(ThumbMaxWidth / pw, ThumbMaxHeight / ph));
            img.Width = Math.Max(1, Math.Round(pw * scale));
            img.Height = Math.Max(1, Math.Round(ph * scale));
            img.MaxWidth = ThumbMaxWidth;
            img.MaxHeight = ThumbMaxHeight;
            img.MouseLeftButtonDown -= Image_MouseLeftButtonDown;
            img.MouseLeftButtonDown += Image_MouseLeftButtonDown;
        }

        public static void HookEditorClicks(RichTextBox box)
        {
            if (box == null) return;
            // IsDocumentEnabled=true 时，点击正文会走到 FlowDocument，
            // VisualTreeHelper 会抛「不是 Visual 或 Visual3D」。插图点击改由编辑器预览事件处理。
            box.IsDocumentEnabled = false;
            box.PreviewMouseLeftButtonDown -= Editor_PreviewMouseLeftButtonDown;
            box.PreviewMouseLeftButtonDown += Editor_PreviewMouseLeftButtonDown;
        }

        private static void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var img = sender as Image;
            if (img == null) return;
            e.Handled = true;
            OpenImagePreview(img);
        }

        private static void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { TryOpenFromEditor(sender as RichTextBox, e); } catch { }
        }

        private static void TryOpenFromEditor(RichTextBox box, MouseButtonEventArgs e)
        {
            if (box == null) return;
            Image img = FindImageFromSource(e.OriginalSource as DependencyObject);
            if (img == null)
                img = HitTestImage(box, e.GetPosition(box));
            if (img == null)
                img = ImageAtPoint(box, e);
            if (img == null) return;
            e.Handled = true;
            OpenImagePreview(img);
        }

        private static Image HitTestImage(RichTextBox box, Point pt)
        {
            Image found = null;
            try
            {
                VisualTreeHelper.HitTest(
                    box,
                    null,
                    delegate(HitTestResult r)
                    {
                        if (r == null) return HitTestResultBehavior.Continue;
                        var img = FindImageFromSource(r.VisualHit as DependencyObject);
                        if (img == null) return HitTestResultBehavior.Continue;
                        found = img;
                        return HitTestResultBehavior.Stop;
                    },
                    new PointHitTestParameters(pt));
            }
            catch { }
            return found;
        }

        private static Image ImageAtPoint(RichTextBox box, MouseButtonEventArgs e)
        {
            foreach (var img in FindImages(box.Document))
            {
                try
                {
                    Point p = e.GetPosition(img);
                    if (p.X >= 0 && p.Y >= 0 && p.X <= img.ActualWidth && p.Y <= img.ActualHeight)
                        return img;
                }
                catch { }
            }
            return null;
        }

        private static Image FindImageFromSource(DependencyObject src)
        {
            while (src != null)
            {
                var img = src as Image;
                if (img != null) return img;
                src = GetParentSafe(src);
            }
            return null;
        }

        private static DependencyObject GetParentSafe(DependencyObject src)
        {
            if (src == null) return null;
            try
            {
                if (src is Visual || src is System.Windows.Media.Media3D.Visual3D)
                    return VisualTreeHelper.GetParent(src);
            }
            catch { }
            var fce = src as FrameworkContentElement;
            if (fce != null) return fce.Parent;
            try
            {
                return LogicalTreeHelper.GetParent(src);
            }
            catch
            {
                return null;
            }
        }

        public static void OpenImagePreview(Image img)
        {
            if (img == null) return;
            int now = Environment.TickCount;
            if (now - _previewTick >= 0 && now - _previewTick < 400) return;
            _previewTick = now;

            string name = img.Tag as string;
            string uid = img.GetValue(ItemUidProperty) as string;
            if (string.IsNullOrEmpty(uid))
            {
                var box = FindRichTextBox(img);
                if (box != null) uid = box.Tag as string;
            }
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(uid)) return;
            string path = Path.Combine(DataPaths.TodoFilesDir(uid), name);
            img.Dispatcher.BeginInvoke(new Action(delegate
            {
                var w = new TodoImagePreviewWindow(path);
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                w.Show();
                w.Activate();
            }));
        }

        private static RichTextBox FindRichTextBox(DependencyObject src)
        {
            while (src != null)
            {
                var box = src as RichTextBox;
                if (box != null) return box;
                src = GetParentSafe(src);
            }
            return null;
        }

        private static BitmapImage LoadBitmap(string path, int decodeWidth)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            if (decodeWidth > 0)
                bmp.DecodePixelWidth = decodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        public static List<string> ListImageFileNames(FlowDocument doc)
        {
            var names = new List<string>();
            foreach (var img in FindImages(doc))
            {
                string n = img.Tag as string;
                if (!string.IsNullOrEmpty(n) && !names.Contains(n))
                    names.Add(n);
            }
            return names;
        }

        private static List<Image> FindImages(FlowDocument doc)
        {
            var list = new List<Image>();
            if (doc == null) return list;
            foreach (var block in doc.Blocks)
                WalkBlock(block, list);
            return list;
        }

        private static void WalkBlock(Block block, List<Image> list)
        {
            var para = block as Paragraph;
            if (para != null)
            {
                foreach (var inline in para.Inlines)
                    WalkInline(inline, list);
                return;
            }
            var listBlock = block as List;
            if (listBlock != null)
            {
                foreach (var item in listBlock.ListItems)
                {
                    foreach (var b in item.Blocks)
                        WalkBlock(b, list);
                }
                return;
            }
            var sec = block as Section;
            if (sec != null)
            {
                foreach (var inner in sec.Blocks)
                    WalkBlock(inner, list);
                return;
            }
            var uiBlock = block as BlockUIContainer;
            if (uiBlock != null)
            {
                var img = uiBlock.Child as Image;
                if (img != null) list.Add(img);
            }
        }

        private static void WalkInline(Inline inline, List<Image> list)
        {
            var container = inline as InlineUIContainer;
            if (container != null)
            {
                var img = container.Child as Image;
                if (img != null) list.Add(img);
                return;
            }
            var span = inline as Span;
            if (span != null)
            {
                foreach (var child in span.Inlines)
                    WalkInline(child, list);
            }
        }
    }
}
