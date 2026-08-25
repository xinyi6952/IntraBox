using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClosedXML.Excel;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.TxtToExcel
{
    /// <summary>
    /// TXT 转 Excel：粘贴文本或选择文件，按分隔符解析为表格预览，导出 .xlsx。
    /// 分隔符探测与解析纯逻辑已抽到 Core.TxtToExcelHelper。
    /// </summary>
    public partial class TxtToExcelView : UserControl, IModuleView
    {
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

        private void LoadFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "选择文本文件", Filter = "文本文件|*.txt;*.csv;*.log|所有文件|*.*" };
            if (dlg.ShowDialog() != true) return;
            string sizeErr;
            if (!SizeLimits.TryCheckFile(dlg.FileName, out sizeErr))
            {
                SetMsg(sizeErr, true);
                return;
            }
            try
            {
                EncodingChoice used;
                bool uncertain;
                InputBox.Text = TextFileCodec.ReadAll(dlg.FileName, TextFileCodec.Auto, SizeLimits.MaxFileBytes, out used, out uncertain);
                SetMsg("", false);
            }
            catch (Exception ex)
            {
                SetMsg("读取失败：" + ex.Message, true);
            }
        }

        private void ParsePreview()
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
            _table = TxtToExcelHelper.Parse(text, sep);
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

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_table == null || _table.Rows.Count == 0) { SetMsg("没有可导出的数据", true); return; }

            var dlg = new SaveFileDialog { Title = "导出 Excel", Filter = "Excel 文件|*.xlsx", FileName = "export.xlsx" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Sheet1");
                    for (int r = 0; r < _table.Rows.Count; r++)
                        for (int c = 0; c < _table.Columns.Count; c++)
                            ws.Cell(r + 1, c + 1).Value = _table.Rows[r][c]?.ToString() ?? "";
                    wb.SaveAs(dlg.FileName);
                }
                SetMsg("已导出到 " + dlg.FileName, false);
            }
            catch (Exception ex)
            {
                SetMsg("导出失败：" + ex.Message, true);
            }
        }

        private void SetMsg(string text, bool isError)
        {
            MsgText.Foreground = isError
                ? (System.Windows.Media.Brush)FindResource("DangerBrush")
                : (System.Windows.Media.Brush)FindResource("OkBrush");
            MsgText.Text = text;
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
