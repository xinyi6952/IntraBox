using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.Notes
{
    public partial class NoteEditorPanel : UserControl
    {
        private static readonly double[] FontSizes = { 10, 12, 14, 16, 18, 20, 24, 28, 32 };
        private bool _loading;
        private bool _dirty;
        private NoteItem _item;
        private DispatcherTimer _saveTimer;
        private DispatcherTimer _previewTimer;
        private int _mdMode;

        public event EventHandler Saved;

        public NoteEditorPanel()
        {
            _loading = true;
            InitializeComponent();
            FontCombo.Items.Add("微软雅黑");
            FontCombo.Items.Add("宋体");
            FontCombo.Items.Add("黑体");
            FontCombo.Items.Add("楷体");
            FontCombo.Items.Add("Consolas");
            FontCombo.Items.Add("Segoe UI");
            FontCombo.SelectedIndex = 0;
            for (int i = 0; i < FontSizes.Length; i++)
                SizeCombo.Items.Add(FontSizes[i].ToString("0"));
            SizeCombo.SelectedIndex = 2;
            NoteRichText.HookEditorClicks(DetailBox);
            DetailBox.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(DetailBox_ScrollChanged), true);
            DetailBox.SizeChanged += (s, e) => KickParaNumbers();
            _saveTimer = new DispatcherTimer();
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); Persist(false); };
            _previewTimer = new DispatcherTimer();
            _previewTimer.Interval = TimeSpan.FromMilliseconds(350);
            _previewTimer.Tick += (s, e) => { _previewTimer.Stop(); RefreshMdPreview(); };
            if (MdBox != null)
            {
                MdBox.EditorControl.WordWrap = true;
                MdBox.SetHighlightingByName("MarkDown");
                MdBox.TextChangedByUser += (s, e) =>
                {
                    if (_loading) return;
                    KickSave();
                    if (_mdMode != 0)
                    {
                        _previewTimer.Stop();
                        _previewTimer.Start();
                    }
                    UpdateStatus();
                };
            }
            ApplyNoteLineNumbers();
            EventHandler onTheme = (s, e) => ApplyNoteLineNumbers();
            ThemeManager.Changed += onTheme;
            Unloaded += (s, e) =>
            {
                ThemeManager.Changed -= onTheme;
                if (_saveTimer != null) _saveTimer.Stop();
                if (_previewTimer != null) _previewTimer.Stop();
            };
            try
            {
                AutoSaveCheck.IsChecked = ConfigManager.Instance.Settings.NotesAutoSave;
            }
            catch
            {
                AutoSaveCheck.IsChecked = false;
            }
            _loading = false;
        }

        public bool IsDirty()
        {
            return _dirty;
        }

        public bool AutoSaveEnabled
        {
            get { return AutoSaveCheck != null && AutoSaveCheck.IsChecked == true; }
        }

        public string CurrentUid
        {
            get { return _item != null ? _item.Uid : null; }
        }

        public void LoadItem(NoteItem item)
        {
            if (item == null) return;
            _loading = true;
            _item = item;
            TitleBox.Text = item.Title ?? "";
            bool md = item.Kind == NoteKind.Markdown;
            RichToolbar.Visibility = md ? Visibility.Collapsed : Visibility.Visible;
            RichHost.Visibility = md ? Visibility.Collapsed : Visibility.Visible;
            MdToolbar.Visibility = md ? Visibility.Visible : Visibility.Collapsed;
            MdHost.Visibility = md ? Visibility.Visible : Visibility.Collapsed;
            DetailBox.Tag = item.Uid;
            if (md)
            {
                string path = DataPaths.NoteBodyMdPath(item.Uid);
                string text = "";
                try
                {
                    if (System.IO.File.Exists(path))
                        text = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                }
                catch { }
                MdBox.Text = text;
                ModeEdit.IsChecked = true;
                ApplyMdMode(0);
            }
            else
            {
                NoteRichText.LoadInto(DetailBox, item.Uid);
                KickParaNumbers();
            }
            _dirty = false;
            _loading = false;
            UpdateStatus();
            UpdateSaveHint();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                _dirty = false;
                UpdateSaveHint();
                KickParaNumbers();
            }), DispatcherPriority.Loaded);
        }

        public void Persist(bool flushStore)
        {
            if (_item == null || _loading) return;
            string title = TitleBox.Text;
            if (string.IsNullOrWhiteSpace(title))
                title = NoteKind.DefaultTitle(_item.Kind);
            _item.Title = title.Trim();
            _item.UpdatedAt = DateTime.Now;
            try
            {
                System.IO.Directory.CreateDirectory(DataPaths.NoteItemDir(_item.Uid));
                if (_item.Kind == NoteKind.Markdown)
                    System.IO.File.WriteAllText(DataPaths.NoteBodyMdPath(_item.Uid), MdBox.Text ?? "", System.Text.Encoding.UTF8);
                else
                    NoteRichText.SaveFrom(DetailBox, _item.Uid);
            }
            catch { }
            NoteStore.UpsertMeta(_item);
            if (flushStore) NoteStore.Flush();
            if (Saved != null) Saved(this, EventArgs.Empty);
            _dirty = false;
            SaveHint.Text = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
        }

        public void FlushNow()
        {
            if (_saveTimer != null) _saveTimer.Stop();
            Persist(true);
        }

        private void KickSave()
        {
            if (_loading || _item == null) return;
            _dirty = true;
            if (!AutoSaveEnabled)
            {
                SaveHint.Text = "未保存";
                return;
            }
            int ms = AppSettings.CurrentHistoryPersistDelayMs();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(ms);
            _saveTimer.Stop();
            _saveTimer.Start();
            SaveHint.Text = "保存中…";
        }

        private void UpdateSaveHint()
        {
            if (SaveHint == null) return;
            if (_dirty)
                SaveHint.Text = AutoSaveEnabled ? "保存中…" : "未保存";
            else
                SaveHint.Text = AutoSaveEnabled ? "自动保存" : "已保存";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Persist(true);
        }

        private void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Persist(true);
        }

        private void Panel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Persist(true);
                e.Handled = true;
            }
        }

        private void AutoSave_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = AutoSaveEnabled;
            try
            {
                ConfigManager.Instance.Settings.NotesAutoSave = on;
                ConfigManager.Instance.Save();
            }
            catch { }
            if (on)
                Persist(true);
            else if (_saveTimer != null)
                _saveTimer.Stop();
            UpdateSaveHint();
        }

        private void ApplyNoteLineNumbers()
        {
            if (MdBox == null || MdBox.EditorControl == null) return;
            var brush = TryFindResource("NotesLineNumberBrush") as Brush;
            if (brush != null)
                MdBox.EditorControl.LineNumbersForeground = brush;
            KickParaNumbers();
        }

        private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            KickSave();
        }

        private void DetailBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            KickSave();
            KickParaNumbers();
            UpdateStatus();
        }

        private void DetailBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateStatus();
        }

        private bool _paraQueued;

        private void DetailBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
                return;
            KickParaNumbers();
        }

        private void KickParaNumbers()
        {
            if (_paraQueued) return;
            _paraQueued = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                _paraQueued = false;
                UpdateParaNumbers();
            }), DispatcherPriority.Loaded);
        }

        private void UpdateParaNumbers()
        {
            if (ParaNumberCanvas == null || DetailBox == null) return;
            ParaNumberCanvas.Children.Clear();
            if (RichHost == null || RichHost.Visibility != Visibility.Visible) return;
            var paras = new List<Paragraph>();
            NoteRichText.CollectParagraphs(DetailBox.Document, paras);
            double h = ParaNumberCanvas.ActualHeight;
            var fg = TryFindResource("NotesLineNumberBrush") as Brush;
            var font = new FontFamily("Consolas");
            for (int i = 0; i < paras.Count; i++)
            {
                Rect rect;
                try
                {
                    rect = paras[i].ContentStart.GetCharacterRect(LogicalDirection.Forward);
                }
                catch
                {
                    continue;
                }
                if (rect.IsEmpty) continue;
                if (h > 1 && (rect.Y + Math.Max(rect.Height, 12) < 0 || rect.Y > h))
                    continue;
                var tb = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    FontFamily = font,
                    FontSize = 11,
                    Foreground = fg,
                    TextAlignment = TextAlignment.Right,
                    Width = 32
                };
                Canvas.SetLeft(tb, 2);
                Canvas.SetTop(tb, rect.Y);
                ParaNumberCanvas.Children.Add(tb);
            }
        }

        private void UpdateStatus()
        {
            if (_item == null)
            {
                StatusText.Text = "";
                return;
            }
            if (_item.Kind == NoteKind.Markdown)
            {
                string t = MdBox.Text ?? "";
                int lines = 1;
                for (int i = 0; i < t.Length; i++)
                    if (t[i] == '\n') lines++;
                StatusText.Text = "第 " + MdBox.EditorControl.TextArea.Caret.Line + " 行 / 共 " + lines + " 行 · " + CountChars(t) + " 字";
            }
            else
            {
                int cur = NoteRichText.CaretParagraphIndex(DetailBox);
                int all = NoteRichText.ParagraphCount(DetailBox.Document);
                string plain = NoteRichText.ToPlain(DetailBox.Document);
                StatusText.Text = "第 " + cur + " 段 / 共 " + all + " 段 · " + CountChars(plain) + " 字";
            }
        }

        private static int CountChars(string t)
        {
            if (string.IsNullOrEmpty(t)) return 0;
            int n = 0;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c != ' ' && c != '\r' && c != '\n' && c != '\t') n++;
            }
            return n;
        }

        private void RichStyle_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || StyleCombo == null) return;
            double size = 14;
            var weight = FontWeights.Normal;
            int i = StyleCombo.SelectedIndex;
            if (i == 1) { size = 24; weight = FontWeights.SemiBold; }
            else if (i == 2) { size = 20; weight = FontWeights.SemiBold; }
            else if (i == 3) { size = 16; weight = FontWeights.SemiBold; }
            DetailBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            DetailBox.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, weight);
            KickSave();
        }

        private void Font_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || FontCombo.SelectedItem == null) return;
            DetailBox.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(FontCombo.SelectedItem.ToString()));
            KickSave();
        }

        private void Size_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || SizeCombo.SelectedItem == null) return;
            double sz;
            if (double.TryParse(SizeCombo.SelectedItem.ToString(), out sz))
                DetailBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, sz);
            KickSave();
        }

        private void Strike_Click(object sender, RoutedEventArgs e)
        {
            DetailBox.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
            KickSave();
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;
            Brush brush;
            if (string.Equals(btn.Tag.ToString(), "default", StringComparison.OrdinalIgnoreCase))
                brush = TryFindResource("EditorForegroundBrush") as Brush ?? TryFindResource("TextPrimaryBrush") as Brush;
            else
            {
                try
                {
                    brush = new BrushConverter().ConvertFromString(btn.Tag.ToString()) as Brush;
                }
                catch
                {
                    brush = null;
                }
            }
            if (brush == null) return;
            DetailBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            KickSave();
        }

        private void Highlight_Click(object sender, RoutedEventArgs e)
        {
            DetailBox.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 235, 59)));
            KickSave();
        }

        private void InsertImage_Click(object sender, RoutedEventArgs e)
        {
            PickImage(false);
        }

        private void MdImage_Click(object sender, RoutedEventArgs e)
        {
            PickImage(true);
        }

        private void PickImage(bool markdown)
        {
            if (_item == null) return;
            var dlg = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp" };
            if (dlg.ShowDialog() != true) return;
            if (markdown)
            {
                string name, err;
                if (!NoteRichText.CopyImage(_item.Uid, dlg.FileName, out name, out err))
                {
                    MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                InsertMd("![](files/" + name + ")");
            }
            else
            {
                string err;
                if (!NoteRichText.TryInsertImage(DetailBox, _item.Uid, dlg.FileName, out err))
                    MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    KickSave();
            }
        }

        private void InsertLink_Click(object sender, RoutedEventArgs e)
        {
            var sel = DetailBox.Selection.Text;
            if (string.IsNullOrEmpty(sel)) sel = "链接";
            var link = new Hyperlink(new Run(sel));
            try { link.NavigateUri = new Uri("https://"); } catch { }
            DetailBox.Selection.Text = "";
            var caret = DetailBox.CaretPosition;
            var para = caret != null ? caret.Paragraph : null;
            if (para != null) para.Inlines.Add(link);
            KickSave();
        }

        private void InsertHr_Click(object sender, RoutedEventArgs e)
        {
            DetailBox.CaretPosition.InsertTextInRun("\r\n————————\r\n");
            KickSave();
        }

        private void InsertTime_Click(object sender, RoutedEventArgs e)
        {
            DetailBox.CaretPosition.InsertTextInRun(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            KickSave();
        }

        private void ClearFormat_Click(object sender, RoutedEventArgs e)
        {
            DetailBox.Selection.ClearAllProperties();
            KickSave();
        }

        private void DetailBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void DetailBox_Drop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0 || _item == null) return;
            string err;
            NoteRichText.TryInsertImage(DetailBox, _item.Uid, files[0], out err);
            KickSave();
        }

        private void MdMode_Click(object sender, RoutedEventArgs e)
        {
            int mode = 0;
            if (ModePreview.IsChecked == true) mode = 1;
            else if (ModeSplit.IsChecked == true) mode = 2;
            ApplyMdMode(mode);
        }

        private void ApplyMdMode(int mode)
        {
            _mdMode = mode;
            if (mode == 0)
            {
                MdEditCol.Width = new GridLength(1, GridUnitType.Star);
                MdSplitCol.Width = new GridLength(0);
                MdPreviewCol.Width = new GridLength(0);
                MdBox.Visibility = Visibility.Visible;
                MdSplitter.Visibility = Visibility.Collapsed;
                MdPreviewWrap.Visibility = Visibility.Collapsed;
            }
            else if (mode == 1)
            {
                MdEditCol.Width = new GridLength(0);
                MdSplitCol.Width = new GridLength(0);
                MdPreviewCol.Width = new GridLength(1, GridUnitType.Star);
                MdBox.Visibility = Visibility.Collapsed;
                MdSplitter.Visibility = Visibility.Collapsed;
                MdPreviewWrap.Visibility = Visibility.Visible;
                RefreshMdPreview();
            }
            else
            {
                MdEditCol.Width = new GridLength(1, GridUnitType.Star);
                MdSplitCol.Width = new GridLength(8);
                MdPreviewCol.Width = new GridLength(1, GridUnitType.Star);
                MdBox.Visibility = Visibility.Visible;
                MdSplitter.Visibility = Visibility.Visible;
                MdPreviewWrap.Visibility = Visibility.Visible;
                RefreshMdPreview();
            }
        }

        private void RefreshMdPreview()
        {
            if (_item == null || MdPreview == null) return;
            try
            {
                MdPreview.NavigateToString(NoteMarkdown.ToPreviewHtml(MdBox.Text, _item.Uid));
            }
            catch { }
        }

        private void MdBold_Click(object sender, RoutedEventArgs e) { WrapMd("**", "**"); }
        private void MdItalic_Click(object sender, RoutedEventArgs e) { WrapMd("*", "*"); }
        private void MdUnderline_Click(object sender, RoutedEventArgs e) { WrapMd("<u>", "</u>"); }
        private void MdStrike_Click(object sender, RoutedEventArgs e) { WrapMd("~~", "~~"); }
        private void MdHr_Click(object sender, RoutedEventArgs e) { InsertMd("\n\n---\n\n"); }
        private void MdQuote_Click(object sender, RoutedEventArgs e) { PrefixMd("> "); }
        private void MdUl_Click(object sender, RoutedEventArgs e) { PrefixMd("- "); }
        private void MdOl_Click(object sender, RoutedEventArgs e) { PrefixMd("1. "); }
        private void MdTask_Click(object sender, RoutedEventArgs e) { PrefixMd("- [ ] "); }
        private void MdTaskDone_Click(object sender, RoutedEventArgs e) { PrefixMd("- [x] "); }
        private void MdInlineCode_Click(object sender, RoutedEventArgs e) { WrapMd("`", "`"); }
        private void MdCodeBlock_Click(object sender, RoutedEventArgs e) { WrapMd("\n```\n", "\n```\n"); }
        private void MdTable_Click(object sender, RoutedEventArgs e)
        {
            InsertMd("\n| 列1 | 列2 | 列3 |\n| --- | --- | --- |\n|  |  |  |\n");
        }

        private void MdHead_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || MdHeadCombo == null) return;
            int i = MdHeadCombo.SelectedIndex;
            if (i <= 0) return;
            string hashes = new string('#', i) + " ";
            PrefixMd(hashes);
            MdHeadCombo.SelectedIndex = 0;
        }

        private void MdLink_Click(object sender, RoutedEventArgs e)
        {
            WrapMd("[", "](https://)");
        }

        private void MdHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "常用语法：\n# 标题  **加粗**  *斜体*  ~~删除线~~\n`代码`  ```代码块```\n- 列表  1. 编号  - [ ] 待办\n[文字](网址)  ![](图片)\nCtrl+F 查找，Ctrl+S 保存。",
                "Markdown 说明", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void WrapMd(string left, string right)
        {
            var ed = MdBox.EditorControl;
            int start = ed.SelectionStart;
            int len = ed.SelectionLength;
            string sel = len > 0 ? ed.Document.GetText(start, len) : "";
            ed.Document.Replace(start, len, left + sel + right);
            ed.Select(start + left.Length, sel.Length);
            KickSave();
        }

        private void InsertMd(string text)
        {
            var ed = MdBox.EditorControl;
            ed.Document.Insert(ed.CaretOffset, text);
            KickSave();
        }

        private void PrefixMd(string prefix)
        {
            var ed = MdBox.EditorControl;
            var line = ed.Document.GetLineByOffset(ed.CaretOffset);
            ed.Document.Insert(line.Offset, prefix);
            KickSave();
        }
    }
}
