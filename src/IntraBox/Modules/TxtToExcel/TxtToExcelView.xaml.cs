using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClosedXML.Excel;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.TxtToExcel
{
    /// <summary>
    /// 文本转 Excel：粘贴或选文件，按 WPS 式分列预览，列格式默认文本，导出 xlsx 或复制到表格软件。
    /// 分列与格式纯逻辑在 Core.TxtToExcelHelper。
    /// </summary>
    public partial class TxtToExcelView : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();
        private DataTable _table;
        private TxtColumnFormat[] _formats;
        private string _pendingFormats;
        private readonly DispatcherTimer _debounce = new DispatcherTimer();
        private bool _restoring;
        private bool _previewMax;

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }

        public TxtToExcelView()
        {
            InitializeComponent();
            _debounce.Interval = TimeSpan.FromMilliseconds(300);
            _debounce.Tick += (s, e) => { _debounce.Stop(); ParsePreview(); };
            InputBox.TextChangedByUser += (s, e) =>
            {
                if (_restoring) return;
                _debounce.Stop();
                _debounce.Start();
            };
            UpdateOptionVisibility();
        }

        private void OnOptionChanged(object sender, RoutedEventArgs e)
        {
            if (ModeCombo == null || SepCombo == null || CustomSepBox == null) return;
            UpdateOptionVisibility();
            if (_restoring) return;
            ParsePreview();
        }

        private void UpdateOptionVisibility()
        {
            int mode = ModeCombo == null ? 0 : ModeCombo.SelectedIndex;
            bool delim = mode <= 0;
            bool key = mode == 2;
            bool width = mode == 3;
            var visDelim = delim ? Visibility.Visible : Visibility.Collapsed;
            var visKey = key ? Visibility.Visible : Visibility.Collapsed;
            var visWidth = width ? Visibility.Visible : Visibility.Collapsed;
            if (SepLabel != null) SepLabel.Visibility = visDelim;
            if (SepCombo != null) SepCombo.Visibility = visDelim;
            if (CustomSepBox != null)
                CustomSepBox.Visibility = delim && SepCombo != null && SepCombo.SelectedIndex == 4
                    ? Visibility.Visible : Visibility.Collapsed;
            if (ConsecutiveCheck != null)
                ConsecutiveCheck.Visibility = delim || key ? Visibility.Visible : Visibility.Collapsed;
            if (KeywordLabel != null) KeywordLabel.Visibility = visKey;
            if (KeywordBox != null) KeywordBox.Visibility = visKey;
            if (WidthLabel != null) WidthLabel.Visibility = visWidth;
            if (WidthBox != null) WidthBox.Visibility = visWidth;
        }

        private async void LoadFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "选择文本文件", Filter = "文本文件|*.txt;*.csv;*.log|所有文件|*.*" };
            if (dlg.ShowDialog() != true) return;
            string sizeErr;
            if (!SizeLimits.TryCheckFile(dlg.FileName, out sizeErr))
            {
                SetMsg(sizeErr, true);
                return;
            }

            string path = dlg.FileName;
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;

            string text;
            EncodingChoice used = TextFileCodec.Auto;
            try
            {
                text = await Task.Run(() => TextFileCodec.ReadAll(path, TextFileCodec.Auto, SizeLimits.MaxFileBytes, out used), token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("读取失败：" + ex.RootMessage(), true); return; }
            if (version != _gate.Version) return;

            InputBox.Text = text;
            SetMsg("", false);
        }

        private async void ParsePreview()
        {
            if (InputBox == null || PreviewGrid == null) return;
            var text = InputBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                _table = null;
                PreviewGrid.ItemsSource = null;
                PreviewGrid.Columns.Clear();
                return;
            }

            string sizeErr;
            if (!SizeLimits.TryCheckText(text, out sizeErr))
            {
                SetMsg(sizeErr, true);
                return;
            }

            var options = BuildOptions(text);

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;

            DataTable table;
            try
            {
                table = await Task.Run(() => TxtToExcelHelper.Parse(text, options), token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("解析失败：" + ex.RootMessage(), true); return; }

            if (version != _gate.Version) return;
            _table = table;
            BindPreview(table);
        }

        private TxtToExcelOptions BuildOptions(string text)
        {
            var opt = new TxtToExcelOptions();
            int mode = ModeCombo == null ? 0 : ModeCombo.SelectedIndex;
            if (mode < 0 || mode > 3) mode = 0;
            opt.Mode = (TxtSplitMode)mode;
            opt.ConsecutiveAsOne = ConsecutiveCheck != null && ConsecutiveCheck.IsChecked == true;
            opt.Keyword = KeywordBox != null ? (KeywordBox.Text ?? "") : "";
            opt.FixedWidths = TxtToExcelHelper.ParseFixedWidths(WidthBox != null ? WidthBox.Text : "");
            opt.Separator = GetSeparator(text);
            return opt;
        }

        private string GetSeparator(string text)
        {
            if (SepCombo == null) return TxtToExcelHelper.DetectSeparator(text ?? "").ToString();
            switch (SepCombo.SelectedIndex)
            {
                case 1: return ",";
                case 2: return "\t";
                case 3: return ";";
                case 4:
                    return CustomSepBox == null || string.IsNullOrEmpty(CustomSepBox.Text)
                        ? "," : CustomSepBox.Text;
                case 5: return " ";
                default: return TxtToExcelHelper.DetectSeparator(text ?? "").ToString();
            }
        }

        private void BindPreview(DataTable table)
        {
            if (_pendingFormats != null)
            {
                _formats = TxtToExcelHelper.ParseFormats(_pendingFormats, table.Columns.Count);
                _pendingFormats = null;
            }
            else
                _formats = TxtToExcelHelper.EnsureFormats(_formats, table.Columns.Count);
            PreviewGrid.ItemsSource = null;
            PreviewGrid.Columns.Clear();
            for (int c = 0; c < table.Columns.Count; c++)
            {
                var col = new DataGridTextColumn();
                col.Header = TxtToExcelHelper.ColumnHeader(c, _formats[c]);
                col.Binding = new Binding(table.Columns[c].ColumnName);
                col.MinWidth = 72;
                col.Width = new DataGridLength(120);
                PreviewGrid.Columns.Add(col);
            }
            PreviewGrid.ItemsSource = table.DefaultView;
        }

        private void RefreshHeaders()
        {
            if (PreviewGrid == null || _formats == null) return;
            int n = Math.Min(PreviewGrid.Columns.Count, _formats.Length);
            for (int c = 0; c < n; c++)
                PreviewGrid.Columns[c].Header = TxtToExcelHelper.ColumnHeader(c, _formats[c]);
        }

        private void PreviewGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            if (FindParent<Thumb>(d) != null) return;
            var header = FindParent<DataGridColumnHeader>(d);
            if (header == null || header.Column == null) return;
            int idx = PreviewGrid.Columns.IndexOf(header.Column);
            ShowFormatMenu(idx, header);
            e.Handled = true;
        }

        private void PreviewGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var cell = FindParent<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell == null || cell.Column == null) return;
            int idx = PreviewGrid.Columns.IndexOf(cell.Column);
            ShowFormatMenu(idx, cell);
            e.Handled = true;
        }

        private void ShowFormatMenu(int col, FrameworkElement target)
        {
            if (col < 0 || _formats == null || col >= _formats.Length || target == null) return;
            var menu = new ContextMenu();
            AddFormatItem(menu, col, TxtColumnFormat.Text);
            AddFormatItem(menu, col, TxtColumnFormat.General);
            AddFormatItem(menu, col, TxtColumnFormat.Number);
            AddFormatItem(menu, col, TxtColumnFormat.DateYmd);
            AddFormatItem(menu, col, TxtColumnFormat.DateMdy);
            AddFormatItem(menu, col, TxtColumnFormat.DateDmy);
            AddFormatItem(menu, col, TxtColumnFormat.Skip);
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AddFormatItem(ContextMenu menu, int col, TxtColumnFormat fmt)
        {
            var item = new MenuItem();
            item.Header = TxtToExcelHelper.FormatName(fmt);
            if (fmt == TxtColumnFormat.Text) item.Header = "文本（默认，保留原文）";
            item.IsCheckable = true;
            item.IsChecked = _formats[col] == fmt;
            var style = TryFindResource("ContextMenuItemPlainStyle") as Style;
            if (style != null) item.Style = style;
            int capturedCol = col;
            TxtColumnFormat capturedFmt = fmt;
            item.Click += (s, e) =>
            {
                _formats[capturedCol] = capturedFmt;
                RefreshHeaders();
            };
            menu.Items.Add(item);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (_table == null || _table.Rows.Count == 0)
            {
                SetMsg("没有可复制的数据", true);
                return;
            }
            string tsv = TxtToExcelHelper.ToTsv(_table, _formats);
            string html = TxtToExcelHelper.ToClipboardHtml(_table, _formats);
            if (string.IsNullOrEmpty(tsv))
            {
                SetMsg("没有可复制的列（均已设为不导入）", true);
                return;
            }
            var data = new DataObject();
            data.SetText(tsv);
            if (!string.IsNullOrEmpty(html))
                data.SetData(DataFormats.Html, html);
            string err;
            if (!ClipboardHelper.TrySetDataObject(data, out err))
            {
                SetMsg(err, true);
                return;
            }
            SetMsg("已复制，可粘贴到 Excel / WPS（按列，文本列保留原文）", false);
        }

        private void MaxBtn_Click(object sender, RoutedEventArgs e)
        {
            _previewMax = !_previewMax;
            InPlaceMaximize.Apply(_previewMax, PreviewHost, 2, 3, ToolbarPanel, InputBox);
            if (IconExpand != null) IconExpand.Visibility = _previewMax ? Visibility.Collapsed : Visibility.Visible;
            if (IconRestore != null) IconRestore.Visibility = _previewMax ? Visibility.Visible : Visibility.Collapsed;
            MaxBtn.ToolTip = _previewMax ? "还原布局" : "最大化预览";
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_table == null || _table.Rows.Count == 0) { SetMsg("没有可导出的数据", true); return; }

            var dlg = new SaveFileDialog { Title = "导出 Excel", Filter = "Excel 文件|*.xlsx", FileName = "export.xlsx" };
            if (dlg.ShowDialog() != true) return;

            string path = dlg.FileName;
            var table = _table;
            var formats = _formats == null ? null : (TxtColumnFormat[])_formats.Clone();

            var included = new List<int>();
            for (int c = 0; c < table.Columns.Count; c++)
            {
                if (TxtToExcelHelper.GetFormat(formats, c) != TxtColumnFormat.Skip)
                    included.Add(c);
            }
            if (included.Count == 0)
            {
                SetMsg("没有可导出的列（均已设为不导入）", true);
                return;
            }

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            SetBusy("导出中…");

            try
            {
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using (var wb = new XLWorkbook())
                    {
                        var ws = wb.Worksheets.Add("Sheet1");
                        for (int r = 0; r < table.Rows.Count; r++)
                        {
                            token.ThrowIfCancellationRequested();
                            for (int i = 0; i < included.Count; i++)
                            {
                                int c = included[i];
                                string raw = table.Rows[r][c] == null ? "" : table.Rows[r][c].ToString();
                                WriteCell(ws.Cell(r + 1, i + 1), raw, TxtToExcelHelper.GetFormat(formats, c));
                            }
                        }
                        wb.SaveAs(path);
                    }
                }, token);
                if (version != _gate.Version) return;
                SetMsg("已导出到 " + path, false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("导出失败：" + ex.RootMessage(), true); }
        }

        private static void WriteCell(IXLCell cell, string raw, TxtColumnFormat format)
        {
            var resolved = TxtToExcelHelper.Resolve(raw, format);
            if (resolved.HasNumber)
            {
                cell.Value = resolved.Number;
                return;
            }
            if (resolved.HasDate)
            {
                cell.Value = resolved.Date;
                cell.Style.NumberFormat.Format = "yyyy-mm-dd";
                return;
            }
            WriteText(cell, resolved.Text);
        }

        private static void WriteText(IXLCell cell, string text)
        {
            if (text == null) text = "";
            cell.Style.NumberFormat.Format = "@";
            cell.SetValue(text);
            cell.SetDataType(XLDataType.Text);
        }

        private void SetMsg(string text, bool isError)
        {
            MsgText.Foreground = isError
                ? (Brush)FindResource("DangerBrush")
                : (Brush)FindResource("OkBrush");
            MsgText.Text = text;
            LoadingOverlay.Hide(this);
        }

        private void SetBusy(string text)
        {
            MsgText.Foreground = (Brush)FindResource("TextSecondaryBrush");
            MsgText.Text = text;
            LoadingOverlay.Show(this, text);
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("txt2excel", out state)) return;

            _restoring = true;
            SetComboIndex(ModeCombo, HistoryManager.GetInt(state, "mode", 0));
            SetComboIndex(SepCombo, HistoryManager.GetInt(state, "sep", 0));
            CustomSepBox.Text = HistoryManager.GetString(state, "customSep");
            ConsecutiveCheck.IsChecked = HistoryManager.GetBool(state, "consecutive", false);
            KeywordBox.Text = HistoryManager.GetString(state, "keyword");
            WidthBox.Text = HistoryManager.GetString(state, "widths");
            _pendingFormats = HistoryManager.GetString(state, "formats");
            InputBox.Text = HistoryManager.GetString(state, "input");
            UpdateOptionVisibility();
            _restoring = false;
            ParsePreview();
        }

        public void OnDeactivated()
        {
            _debounce.Stop();
            CancelPending();
            HistoryManager.Save("txt2excel", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" },
                { "mode", ModeCombo.SelectedIndex },
                { "sep", SepCombo.SelectedIndex },
                { "customSep", CustomSepBox.Text ?? "" },
                { "consecutive", ConsecutiveCheck.IsChecked == true },
                { "keyword", KeywordBox.Text ?? "" },
                { "widths", WidthBox.Text ?? "" },
                { "formats", TxtToExcelHelper.FormatsToString(_formats) }
            });
        }

        private static void SetComboIndex(ComboBox combo, int index)
        {
            if (combo == null || combo.Items.Count == 0) return;
            if (index < 0 || index >= combo.Items.Count) return;
            combo.SelectedIndex = index;
        }

        private static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                var match = d as T;
                if (match != null) return match;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }
    }
}
