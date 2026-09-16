using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IntraBox.Controls;
using IntraBox.Core;
using IntraBox.Modules.Todo;

namespace IntraBox.Modules.WorkCal
{
    /// <summary>工作日历：法定假月历、待办投影；时区换算与工作日推算默认收起。</summary>
    public partial class WorkCalView : UserControl, IModuleView
    {
        private int _year;
        private int _month;
        private DateTime _selected;
        private List<TodoItem> _todos = new List<TodoItem>();
        private bool _tzReady;

        private bool _uiReady;

        public WorkCalView()
        {
            InitializeComponent();
            var now = DateTime.Now;
            _year = now.Year;
            _month = now.Month;
            _selected = now.Date;
            WorkFromDate.SelectedDate = now.Date;
            FromTimeBox.Text = now.ToString("yyyy-MM-dd HH:mm:ss");
            FillTimeZones();
            _uiReady = true;
        }

        public void OnActivated()
        {
            WorkcalStore.Reload();
            Dictionary<string, object> state;
            if (HistoryManager.TryLoad("workcal", out state))
            {
                string localId = TimeZoneInfo.Local.Id;
                string from = HistoryManager.GetString(state, "tzFrom");
                string to = HistoryManager.GetString(state, "tzTo");
                TzFromCombo.SelectId(string.IsNullOrEmpty(from) ? localId : from);
                TzToCombo.SelectId(string.IsNullOrEmpty(to) ? localId : to);
                ToolsExpander.IsExpanded = HistoryManager.GetBool(state, "toolsOpen", false);
            }
            RefreshAll();
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("workcal", new Dictionary<string, object>
            {
                { "tzFrom", TzFromCombo.SelectedId },
                { "tzTo", TzToCombo.SelectedId },
                { "toolsOpen", ToolsExpander.IsExpanded }
            });
        }

        private void ToolsExpander_Changed(object sender, RoutedEventArgs e)
        {
            if (!_uiReady) return;
            BuildCalendar();
        }

        private bool MarksCompact
        {
            get { return ToolsExpander != null && ToolsExpander.IsExpanded; }
        }

        private void FillTimeZones()
        {
            var zones = TimeZoneInfo.GetSystemTimeZones();
            var items = new List<SearchCombo.Item>(zones.Count);
            string localId = TimeZoneInfo.Local.Id;
            for (int i = 0; i < zones.Count; i++)
            {
                var tz = zones[i];
                items.Add(new SearchCombo.Item { Id = tz.Id, Text = tz.DisplayName, Extra = tz.Id });
            }
            TzFromCombo.SetItems(items);
            TzToCombo.SetItems(items);
            TzFromCombo.SelectId(localId);
            TzToCombo.SelectId(localId);
            _tzReady = true;
        }

        private void RefreshAll()
        {
            _todos = TodoStore.Snapshot();
            UpdateNowStrip();
            UpdateYearHint();
            BuildCalendar();
            RefreshDayPanel();
        }

        private void UpdateNowStrip()
        {
            var now = DateTimeOffset.Now;
            string utc = now.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
            string local = now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " 本机";
            string bj = "";
            try
            {
                var cn = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                bj = TimeZoneInfo.ConvertTime(now, cn).ToString("yyyy-MM-dd HH:mm:ss") + " 北京";
            }
            catch
            {
                bj = "";
            }
            NowStrip.Text = string.IsNullOrEmpty(bj) ? (local + "  ·  " + utc) : (local + "  ·  " + bj + "  ·  " + utc);
        }

        private void UpdateYearHint()
        {
            if (WorkdayHelper.HasBuiltinYear(_year))
                YearHint.Text = "内置 " + _year + " 年大陆放假表（国务院通知，当前含 "
                    + WorkdayCnHolidays.YearsText() + "）。点格子可覆盖某天。";
            else
                YearHint.Text = _year + " 年尚无内置放假表（国务院通知公布后随 IntraBox 升级写入，不联网）。暂按周末休息，可把某天标成放假或补班。";
        }

        private void FillNow_Click(object sender, RoutedEventArgs e)
        {
            FromTimeBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ConvertTz_Click(sender, e);
        }

        private void ConvertTz_Click(object sender, RoutedEventArgs e)
        {
            if (!_tzReady) return;
            DateTime local;
            if (!DateTime.TryParse(FromTimeBox.Text, out local))
            {
                FormHint.Apply(TzResult, "无法解析时间");
                return;
            }
            TimeZoneInfo from;
            TimeZoneInfo to;
            if (!TryZone(TzFromCombo.SelectedId, out from) || !TryZone(TzToCombo.SelectedId, out to))
            {
                FormHint.Apply(TzResult, "时区无效");
                return;
            }
            try
            {
                var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
                var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, from);
                var dest = TimeZoneInfo.ConvertTimeFromUtc(utc, to);
                FormHint.Apply(TzResult, dest.ToString("yyyy-MM-dd HH:mm:ss") + "  " + to.DisplayName);
            }
            catch (Exception ex)
            {
                FormHint.Apply(TzResult, ex.RootMessage());
            }
        }

        private static bool TryZone(string id, out TimeZoneInfo tz)
        {
            tz = null;
            if (string.IsNullOrEmpty(id)) return false;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(id);
                return tz != null;
            }
            catch
            {
                return false;
            }
        }

        private void Workdays_Click(object sender, RoutedEventArgs e)
        {
            DateTime? start = WorkFromDate.SelectedDate;
            int n;
            if (!start.HasValue)
            {
                FormHint.Apply(WorkResult, "请选基准日");
                return;
            }
            if (!int.TryParse((WorkNBox.Text ?? "").Trim(), out n))
            {
                FormHint.Apply(WorkResult, "请输入整数（可为负）");
                return;
            }
            if (n > 400 || n < -400)
            {
                FormHint.Apply(WorkResult, "工作日数量过大");
                return;
            }
            var d = WorkdayHelper.AddWorkdays(start.Value, n);
            string word = n >= 0 ? "后" : "前";
            int abs = n >= 0 ? n : -n;
            FormHint.Apply(WorkResult, abs + " 个工作日" + word + "是 " + d.ToString("yyyy-MM-dd") +
                              "（" + (WorkdayHelper.IsWorkday(d) ? "工作日" : "休息日") + "）");
            _year = d.Year;
            _month = d.Month;
            _selected = d;
            UpdateYearHint();
            BuildCalendar();
            RefreshDayPanel();
        }

        private void CalPrev_Click(object sender, RoutedEventArgs e)
        {
            _month--;
            if (_month < 1) { _month = 12; _year--; }
            UpdateYearHint();
            BuildCalendar();
        }

        private void CalNext_Click(object sender, RoutedEventArgs e)
        {
            _month++;
            if (_month > 12) { _month = 1; _year++; }
            UpdateYearHint();
            BuildCalendar();
        }

        private void BuildCalendar()
        {
            CalTitle.Text = _year + " 年 " + _month + " 月";
            CalGrid.Children.Clear();
            string[] names = { "日", "一", "二", "三", "四", "五", "六" };
            for (int i = 0; i < 7; i++)
            {
                var head = new TextBlock
                {
                    Text = names[i],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 4, 0, 8)
                };
                if (i == 0 || i == 6)
                    head.Foreground = WeekendBrush();
                CalGrid.Children.Add(head);
            }
            var first = new DateTime(_year, _month, 1);
            int offset = (int)first.DayOfWeek;
            int days = DateTime.DaysInMonth(_year, _month);
            for (int cell = 0; cell < 42; cell++)
            {
                int day = cell - offset + 1;
                if (day < 1 || day > days)
                {
                    CalGrid.Children.Add(new Border { MinHeight = MarksCompact ? 32 : 48 });
                    continue;
                }
                CalGrid.Children.Add(CreateDayCell(new DateTime(_year, _month, day)));
            }
        }

        private FrameworkElement CreateDayCell(DateTime date)
        {
            bool weekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
            var root = new Button();
            root.Style = TryFindResource("CalDayButton") as Style;
            root.Margin = new Thickness(2);
            root.Padding = new Thickness(2);
            root.Tag = date;
            root.Click += CalDay_Click;
            root.ClipToBounds = false;
            root.MinHeight = MarksCompact ? 32 : 48;
            if (_selected.Date == date)
                root.BorderBrush = WeekendBrush();

            var cell = new Grid { ClipToBounds = false };

            var dayText = new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = MarksCompact ? 13 : 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (weekend)
                dayText.Foreground = WeekendBrush();
            else
                dayText.Foreground = TryBrush("TextPrimaryBrush", Brushes.Gainsboro);
            cell.Children.Add(dayText);

            if (!MarksCompact)
            {
                var badge = CreateMarkBadge(WorkdayHelper.EffectiveMark(date));
                if (badge != null)
                    cell.Children.Add(badge);

                var dayTodos = TodosOn(date);
                int cap = dayTodos.Count < 3 ? dayTodos.Count : 3;
                if (cap > 0)
                {
                    var dots = new WrapPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 1)
                    };
                    for (int i = 0; i < cap; i++)
                    {
                        dots.Children.Add(new Ellipse
                        {
                            Width = 6,
                            Height = 6,
                            Margin = new Thickness(1.5, 0, 1.5, 0),
                            Fill = MarkerBrush(dayTodos[i])
                        });
                    }
                    cell.Children.Add(dots);
                }
            }

            root.Content = cell;
            return root;
        }

        private Brush WeekendBrush()
        {
            return TryBrush("AccentBrush", Brushes.SteelBlue);
        }

        private FrameworkElement CreateMarkBadge(WorkcalMark mark)
        {
            string label;
            Brush fill;
            if (mark == WorkcalMark.Off)
            {
                label = "休";
                fill = WeekendBrush();
            }
            else if (mark == WorkcalMark.Work)
            {
                label = "班";
                fill = TryBrush("DangerBrush", Brushes.IndianRed);
            }
            else return null;

            var g = new Grid
            {
                Width = 14,
                Height = 14,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };
            g.Children.Add(new Ellipse
            {
                Width = 14,
                Height = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = fill
            });
            g.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryBrush("PrimaryButtonFgBrush", Brushes.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return g;
        }

        private List<TodoItem> TodosOn(DateTime date)
        {
            var list = new List<TodoItem>();
            for (int i = 0; i < _todos.Count; i++)
            {
                var it = _todos[i];
                if (it == null || !it.DueAt.HasValue || it.DueAt.Value.Date != date) continue;
                list.Add(it);
            }
            list.Sort(CompareTodoDot);
            return list;
        }

        private static int CompareTodoDot(TodoItem a, TodoItem b)
        {
            return TodoDotRank(a).CompareTo(TodoDotRank(b));
        }

        private static int TodoDotRank(TodoItem it)
        {
            if (it == null || it.Completed) return 4;
            if (it.Priority == TodoPriority.High) return 0;
            if (it.Priority == TodoPriority.Low) return 2;
            return 1;
        }

        private Brush MarkerBrush(TodoItem it)
        {
            if (it.Completed)
                return TryBrush("SuccessBrush", Brushes.SeaGreen);
            if (it.Priority == TodoPriority.High)
                return TryBrush("DangerBrush", Brushes.IndianRed);
            if (it.Priority == TodoPriority.Low)
                return TryBrush("TextSecondaryBrush", Brushes.Gray);
            return TryBrush("AccentBrush", Brushes.SteelBlue);
        }

        private Brush TryBrush(string key, Brush fallback)
        {
            var b = TryFindResource(key) as Brush;
            return b ?? fallback;
        }

        private void CalDay_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || !(btn.Tag is DateTime)) return;
            _selected = ((DateTime)btn.Tag).Date;
            BuildCalendar();
            RefreshDayPanel();
        }

        private void RefreshDayPanel()
        {
            bool work = WorkdayHelper.IsWorkday(_selected);
            var em = WorkdayHelper.EffectiveMark(_selected);
            string kind = work ? "工作日" : "休息日";
            if (em == WorkcalMark.Off) kind += "（放假）";
            else if (em == WorkcalMark.Work) kind += "（补班）";
            DayTitle.Text = _selected.ToString("yyyy-MM-dd") + "  " + kind;
            var rows = new List<DayTodoRow>();
            for (int i = 0; i < _todos.Count; i++)
            {
                var it = _todos[i];
                if (it == null || !it.DueAt.HasValue || it.DueAt.Value.Date != _selected) continue;
                string plain = "";
                try
                {
                    string path = DataPaths.TodoDetailsPath(it.Uid);
                    if (System.IO.File.Exists(path))
                    {
                        using (var fs = System.IO.File.OpenRead(path))
                        {
                            var doc = System.Windows.Markup.XamlReader.Load(fs) as System.Windows.Documents.FlowDocument;
                            plain = TodoRichText.ToPlain(doc);
                        }
                    }
                }
                catch { }
                rows.Add(new DayTodoRow
                {
                    Uid = it.Uid,
                    Title = TodoRichText.ExtractTitle(plain),
                    DueText = it.DueAt.Value.ToString("HH:mm") + (it.Completed ? " 已办" : "")
                });
            }
            DayList.ItemsSource = rows;
            DayMsg.Text = rows.Count == 0 ? "当天没有计划完成的待办。" : "双击打开任务计划。";
        }

        private void MarkOff_Click(object sender, RoutedEventArgs e)
        {
            SetMark(WorkcalMark.Off);
        }

        private void MarkWork_Click(object sender, RoutedEventArgs e)
        {
            SetMark(WorkcalMark.Work);
        }

        private void MarkClear_Click(object sender, RoutedEventArgs e)
        {
            SetMark(WorkcalMark.None);
        }

        private void SetMark(WorkcalMark mark)
        {
            string err;
            if (!WorkcalStore.SetMark(_selected, mark, out err))
            {
                DayMsg.Text = err;
                return;
            }
            BuildCalendar();
            RefreshDayPanel();
        }

        private void NewTodo_Click(object sender, RoutedEventArgs e)
        {
            var main = Window.GetWindow(this) as IntraBox.MainWindow;
            if (main == null) return;
            main.OpenTodoOnDate(_selected);
        }

        private void DayList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = DayList.SelectedItem as DayTodoRow;
            if (row == null) return;
            var main = Window.GetWindow(this) as IntraBox.MainWindow;
            if (main == null) return;
            main.OpenTodo(row.Uid);
        }

        private sealed class DayTodoRow
        {
            public string Uid { get; set; }
            public string Title { get; set; }
            public string DueText { get; set; }
        }
    }
}
