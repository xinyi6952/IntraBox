using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IntraBox.Core;
using IntraBox.Modules.Todo;

namespace IntraBox.Modules.Notes
{
    public static class NoteRichText
    {
        private const double ThumbMaxWidth = 200;
        private const double ThumbMaxHeight = 120;
        private const int ThumbDecodeWidth = 400;

        private static readonly DependencyProperty ItemUidProperty =
            DependencyProperty.RegisterAttached("ItemUid", typeof(string), typeof(NoteRichText), new PropertyMetadata(null));

        public static FlowDocument CreateEmptyDocument()
        {
            var doc = new FlowDocument();
            doc.FontFamily = new FontFamily("Microsoft YaHei, 微软雅黑, Segoe UI");
            doc.FontSize = 14;
            doc.Blocks.Add(new Paragraph());
            return doc;
        }

        public static FlowDocument CreateSampleDocument()
        {
            var doc = new FlowDocument();
            doc.FontFamily = new FontFamily("Microsoft YaHei, 微软雅黑, Segoe UI");
            doc.FontSize = 14;
            doc.Blocks.Add(Heading("【示例】本周联调纪要（普通笔记）", 22));
            doc.Blocks.Add(Callout("何时用普通笔记：开会时要记结论、用颜色/高亮标重点、或当场插一张截图对照。长期手册、命令和待办清单请看另一篇 Markdown 示例。本篇可删。"));
            doc.Blocks.Add(Heading("怎么用", 16));
            doc.Blocks.Add(Bullets(
                "改顶部标题；点「保存」或 Ctrl+S。默认不自动保存，需要可勾选自动保存。",
                "工具栏：加粗 / 列表 / 色点 / 高亮 / 插图（也可把 png、jpg 拖进正文）。",
                "左侧段落号对应当前段，底栏看段数和字数。",
                "右上角 × 关闭抽屉；有未保存改动会询问。"));
            doc.Blocks.Add(Heading("场景：支付组周会（对照填写）", 16));
            doc.Blocks.Add(Body("主题：支付回调联调。参加：网关、交易、测试。"));
            var concl = new Paragraph();
            concl.Margin = new Thickness(0, 0, 0, 6);
            concl.Inlines.Add(new Run("结论："));
            var hi = new Run("回调超时改为 8 秒，重试 1 次。");
            hi.Background = new SolidColorBrush(Color.FromRgb(255, 235, 59));
            hi.Foreground = new SolidColorBrush(Color.FromRgb(43, 43, 43));
            concl.Inlines.Add(hi);
            doc.Blocks.Add(concl);
            doc.Blocks.Add(Body("待确认：测试机 Hosts 是否指向 pay-gateway-test；联调记录放到共享目录。"));
            return doc;
        }

        private static Paragraph Heading(string text, double size)
        {
            var p = new Paragraph(new Run(text ?? ""));
            p.FontSize = size;
            p.FontWeight = FontWeights.SemiBold;
            p.Margin = new Thickness(0, 0, 0, 8);
            return p;
        }

        private static Paragraph Body(string text)
        {
            var p = new Paragraph(new Run(text ?? ""));
            p.Margin = new Thickness(0, 0, 0, 6);
            return p;
        }

        private static Paragraph Callout(string text)
        {
            var run = new Run(text ?? "");
            run.Foreground = new SolidColorBrush(Color.FromRgb(74, 136, 199));
            var p = new Paragraph(run);
            p.Margin = new Thickness(0, 0, 0, 10);
            return p;
        }

        private static System.Windows.Documents.List Bullets(params string[] items)
        {
            var list = new System.Windows.Documents.List();
            list.MarkerStyle = TextMarkerStyle.Disc;
            list.Margin = new Thickness(0, 0, 0, 10);
            if (items == null) return list;
            for (int i = 0; i < items.Length; i++)
                list.ListItems.Add(new ListItem(new Paragraph(new Run(items[i]))));
            return list;
        }

        public static void SaveDocument(FlowDocument doc, string uid)
        {
            if (doc == null || string.IsNullOrEmpty(uid)) return;
            foreach (var img in FindImages(doc))
                img.Source = null;
            Directory.CreateDirectory(DataPaths.NoteItemDir(uid));
            using (var fs = File.Create(DataPaths.NoteBodyXamlPath(uid)))
                XamlWriter.Save(doc, fs);
        }

        public static void LoadInto(RichTextBox box, string uid)
        {
            if (box == null) return;
            string path = DataPaths.NoteBodyXamlPath(uid);
            if (!File.Exists(path))
            {
                box.Document = CreateEmptyDocument();
                return;
            }
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var doc = XamlReader.Load(fs) as FlowDocument;
                    if (doc == null)
                    {
                        box.Document = CreateEmptyDocument();
                        return;
                    }
                    RestoreImages(doc, uid);
                    box.Document = doc;
                }
            }
            catch
            {
                box.Document = CreateEmptyDocument();
            }
        }

        public static void SaveFrom(RichTextBox box, string uid)
        {
            if (box == null || string.IsNullOrEmpty(uid)) return;
            var doc = box.Document;
            SaveDocument(doc, uid);
            RestoreImages(doc, uid);
        }

        public static string ToPlain(FlowDocument doc)
        {
            if (doc == null) return "";
            return new TextRange(doc.ContentStart, doc.ContentEnd).Text ?? "";
        }

        public static int ParagraphCount(FlowDocument doc)
        {
            var list = new List<Paragraph>();
            CollectParagraphs(doc, list);
            return list.Count < 1 ? 1 : list.Count;
        }

        public static void CollectParagraphs(FlowDocument doc, List<Paragraph> list)
        {
            if (list == null) return;
            list.Clear();
            if (doc == null) return;
            CollectBlocks(doc.Blocks, list);
        }

        private static void CollectBlocks(BlockCollection blocks, List<Paragraph> list)
        {
            if (blocks == null) return;
            foreach (var b in blocks)
            {
                var p = b as Paragraph;
                if (p != null)
                {
                    list.Add(p);
                    continue;
                }
                var lst = b as System.Windows.Documents.List;
                if (lst == null) continue;
                foreach (var item in lst.ListItems)
                    CollectBlocks(item.Blocks, list);
            }
        }

        public static int CaretParagraphIndex(RichTextBox box)
        {
            if (box == null || box.Document == null) return 1;
            var caret = box.CaretPosition;
            if (caret == null) return 1;
            var list = new List<Paragraph>();
            CollectParagraphs(box.Document, list);
            for (int i = 0; i < list.Count; i++)
            {
                if (caret.CompareTo(list[i].ContentEnd) <= 0)
                    return i + 1;
            }
            return Math.Max(1, list.Count);
        }

        public static bool TryInsertImage(RichTextBox box, string uid, string srcFile, out string error)
        {
            error = null;
            string name;
            if (!CopyImage(uid, srcFile, out name, out error))
                return false;
            BitmapImage bmp;
            try { bmp = LoadBitmap(Path.Combine(DataPaths.NoteFilesDir(uid), name), ThumbDecodeWidth); }
            catch { error = "无法读取图片"; return false; }
            var img = new Image();
            img.Tag = name;
            img.Source = bmp;
            ApplyThumb(img, uid);
            var container = new InlineUIContainer(img);
            var caret = box.CaretPosition ?? box.Document.ContentEnd;
            var para = caret.Paragraph;
            if (para == null)
            {
                para = new Paragraph();
                box.Document.Blocks.Add(para);
            }
            para.Inlines.Add(container);
            return true;
        }

        public static bool CopyImage(string uid, string srcFile, out string name, out string error)
        {
            name = null;
            error = null;
            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(srcFile))
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
            if (!info.Exists) { error = "文件不存在"; return false; }
            if (info.Length > SizeLimits.MaxFileBytes)
            {
                error = "图片超过设置中的文件大小上限";
                return false;
            }
            string dir = DataPaths.NoteFilesDir(uid);
            Directory.CreateDirectory(dir);
            name = Guid.NewGuid().ToString("N") + ext.ToLowerInvariant();
            File.Copy(srcFile, Path.Combine(dir, name), true);
            return true;
        }

        public static bool IsAllowedImageExt(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp";
        }

        public static void HookEditorClicks(RichTextBox box)
        {
            if (box == null) return;
            box.IsDocumentEnabled = false;
            box.PreviewMouseLeftButtonDown -= Editor_PreviewMouseLeftButtonDown;
            box.PreviewMouseLeftButtonDown += Editor_PreviewMouseLeftButtonDown;
        }

        private static void RestoreImages(FlowDocument doc, string uid)
        {
            string dir = DataPaths.NoteFilesDir(uid);
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
            var src = img.Source as BitmapSource;
            double pw = src != null && src.PixelWidth > 0 ? src.PixelWidth : ThumbMaxWidth;
            double ph = src != null && src.PixelHeight > 0 ? src.PixelHeight : ThumbMaxHeight;
            double scale = Math.Min(1.0, Math.Min(ThumbMaxWidth / pw, ThumbMaxHeight / ph));
            img.Width = Math.Max(1, Math.Round(pw * scale));
            img.Height = Math.Max(1, Math.Round(ph * scale));
            img.MaxWidth = ThumbMaxWidth;
            img.MaxHeight = ThumbMaxHeight;
        }

        private static void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var box = sender as RichTextBox;
                if (box == null) return;
                Image img = FindImageFromSource(e.OriginalSource as DependencyObject);
                if (img == null) return;
                e.Handled = true;
                string name = img.Tag as string;
                string uid = img.GetValue(ItemUidProperty) as string;
                if (string.IsNullOrEmpty(uid)) uid = box.Tag as string;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(uid)) return;
                string path = Path.Combine(DataPaths.NoteFilesDir(uid), name);
                box.Dispatcher.BeginInvoke(new Action(delegate
                {
                    var w = new TodoImagePreviewWindow(path);
                    w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    w.Show();
                }));
            }
            catch { }
        }

        private static Image FindImageFromSource(DependencyObject src)
        {
            while (src != null)
            {
                var img = src as Image;
                if (img != null) return img;
                var fce = src as FrameworkContentElement;
                if (src is Visual)
                {
                    try { src = VisualTreeHelper.GetParent(src); }
                    catch { src = fce != null ? fce.Parent : LogicalTreeHelper.GetParent(src); }
                }
                else if (fce != null) src = fce.Parent;
                else
                {
                    try { src = LogicalTreeHelper.GetParent(src); }
                    catch { return null; }
                }
            }
            return null;
        }

        private static BitmapImage LoadBitmap(string path, int decodeWidth)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
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
                    foreach (var b in item.Blocks)
                        WalkBlock(b, list);
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
