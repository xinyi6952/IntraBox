using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClosedXML.Excel;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.TxtToExcel
{
    /// <summary>
    /// 文本转 Excel：粘贴文本或选择文件，按分隔符解析为表格预览，导出 .xlsx。
    /// 分隔符探测与解析纯逻辑已抽到 Core.TxtToExcelHelper。
    /// </summary>
    public partial class TxtToExcelView : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }

        private DataTable _table;
        private readonly DispatcherTimer _debounce = new DispatcherTimer();
        private bool _restoring;

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
        }

        private void OnOptionChanged(object sender, RoutedEventArgs e)
        {
            // XAML 初始化期间事件可能先于其它命名元素触发，加空值保护
            if (SepCombo == null || CustomSepBox == null) return;
            CustomSepBox.Visibility = SepCombo.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
            if (_restoring) return;
            ParsePreview();
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
            if (string.IsNullOrWhiteSpace(text)) { _table = null; PreviewGrid.ItemsSource = null; return; }

            string sizeErr;
            if (!SizeLimits.TryCheckText(text, out sizeErr))
            {
                SetMsg(sizeErr, true);
                return;
            }

            char sep = GetSeparator(text);

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;

            DataTable table;
            try
            {
                // 解析建表放后台线程，大文本不卡
                table = await Task.Run(() => TxtToExcelHelper.Parse(text, sep), token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("解析失败：" + ex.RootMessage(), true); return; }

            if (version != _gate.Version) return;
            _table = table;
            PreviewGrid.ItemsSource = _table.DefaultView;
        }

        private char GetSeparator(string text)
        {
            switch (SepCombo.SelectedIndex)
            {
                case 1: return ',';
                case 2: return '\t';
                case 3: return ';';
                case 4: return string.IsNullOrEmpty(CustomSepBox.Text) ? ',' : CustomSepBox.Text[0];
                default: return TxtToExcelHelper.DetectSeparator(text);
            }
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_table == null || _table.Rows.Count == 0) { SetMsg("没有可导出的数据", true); return; }

            var dlg = new SaveFileDialog { Title = "导出 Excel", Filter = "Excel 文件|*.xlsx", FileName = "export.xlsx" };
            if (dlg.ShowDialog() != true) return;

            string path = dlg.FileName;
            var table = _table; // 捕获引用，后台只读

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
                            for (int c = 0; c < table.Columns.Count; c++)
                                ws.Cell(r + 1, c + 1).Value = table.Rows[r][c]?.ToString() ?? "";
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

        private void SetMsg(string text, bool isError)
        {
            MsgText.Foreground = isError
                ? (System.Windows.Media.Brush)FindResource("DangerBrush")
                : (System.Windows.Media.Brush)FindResource("OkBrush");
            MsgText.Text = text;
            LoadingOverlay.Hide(this);
        }

        private void SetBusy(string text)
        {
            MsgText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
            MsgText.Text = text;
            LoadingOverlay.Show(this, text);
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("txt2excel", out state)) return;

            _restoring = true;
            SetComboIndex(SepCombo, HistoryManager.GetInt(state, "sep", 0));
            CustomSepBox.Text = HistoryManager.GetString(state, "customSep");
            CustomSepBox.Visibility = SepCombo.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
            InputBox.Text = HistoryManager.GetString(state, "input");
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
                { "sep", SepCombo.SelectedIndex },
                { "customSep", CustomSepBox.Text ?? "" }
            });
        }

        private static void SetComboIndex(ComboBox combo, int index)
        {
            if (combo == null || combo.Items.Count == 0) return;
            if (index < 0 || index >= combo.Items.Count) return;
            combo.SelectedIndex = index;
        }
    }
}
