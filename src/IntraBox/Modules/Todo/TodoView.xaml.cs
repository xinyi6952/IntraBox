using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.Todo
{
    public partial class TodoView : UserControl, IModuleView, ILeaveGuard
    {
        public static string PendingOpenUid;

        private readonly List<TodoRow> _rows = new List<TodoRow>();
        private bool _loading;
        private int _calYear;
        private int _calMonth;
        private DateTime? _calFilterDay;
        private bool _drawerMax;
        private bool _skipListClick;
        private string _sortKey = "created";
        private bool _sortAsc;
        private bool _chromeLoading;

        public TodoView()
        {
            InitializeComponent();
            var now = DateTime.Now;
            _calYear = now.Year;
            _calMonth = now.Month;
            UpdateSortHeaders();
            SizeChanged += (s, e) => ApplyDrawerSize();
            DrawerEditor.Saved += (s, e) =>
            {
                RefreshList();
                SelectUid(DrawerEditor.CurrentUid);
                if (ReadOnlyCheck != null && !DrawerEditor.IsCompletedLocked && !DrawerEditor.IsNewItem)
                    ReadOnlyCheck.IsEnabled = true;
            };
        }

        public void OnActivated()
        {
            RefreshList();
            OpenPending();
        }

        public void OnDeactivated()
        {
            TodoStore.Flush();
        }

        public bool CanLeave()
        {
            return CloseDrawer();
        }

        public void OpenPending()
        {
            string uid = PendingOpenUid;
            PendingOpenUid = null;
            if (string.IsNullOrEmpty(uid)) return;
            var item = TodoStore.GetByUid(uid);
            if (item == null) return;
            if (item.Completed) TabDone.IsChecked = true;
            else TabTodo.IsChecked = true;
            RefreshList();
            SelectUid(uid);
            OpenDrawer(uid);
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            if (!CloseDrawer()) return;
            TabTodo.IsChecked = true;
            var item = TodoItem.CreateNew(TodoStore.NextAutoId());
            OpenDrawerItem(item, true);
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (!CloseDrawer()) return;
            RefreshList();
        }

        private void ModeList_Click(object sender, RoutedEventArgs e)
        {
            ListHost.Visibility = Visibility.Visible;
            CalPanel.Visibility = Visibility.Collapsed;
        }

        private void ModeCal_Click(object sender, RoutedEventArgs e)
        {
            if (!CloseDrawer()) return;
            ListHost.Visibility = Visibility.Collapsed;
            CalPanel.Visibility = Visibility.Visible;
            BuildCalendar();
        }

        private void CalPrev_Click(object sender, RoutedEventArgs e)
        {
            _calMonth--;
            if (_calMonth < 1) { _calMonth = 12; _calYear--; }
            BuildCalendar();
        }

        private void CalNext_Click(object sender, RoutedEventArgs e)
        {
            _calMonth++;
            if (_calMonth > 12) { _calMonth = 1; _calYear++; }
            BuildCalendar();
        }

        private void BuildCalendar()
        {
            CalTitle.Text = _calYear + " 年 " + _calMonth + " 月";
            CalGrid.Children.Clear();
            string[] names = { "日", "一", "二", "三", "四", "五", "六" };
            for (int i = 0; i < 7; i++)
            {
                CalGrid.Children.Add(new TextBlock
                {
                    Text = names[i],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 4, 0, 8)
                });
            }
            var first = new DateTime(_calYear, _calMonth, 1);
            int offset = (int)first.DayOfWeek;
            int days = DateTime.DaysInMonth(_calYear, _calMonth);
            var items = TodoStore.Snapshot();
            for (int cell = 0; cell < 42; cell++)
            {
                int day = cell - offset + 1;
                if (day < 1 || day > days)
                {
                    CalGrid.Children.Add(new TextBlock());
                    continue;
                }
                var date = new DateTime(_calYear, _calMonth, day);
                CalGrid.Children.Add(CreateDayCell(date, items));
            }
        }

        private FrameworkElement CreateDayCell(DateTime date, List<TodoItem> items)
        {
            var root = new Button();
            root.Margin = new Thickness(2);
            root.Padding = new Thickness(2);
            root.Tag = date;
            root.Click += CalDay_Click;
            root.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            root.VerticalContentAlignment = VerticalAlignment.Stretch;
            if (_calFilterDay.HasValue && _calFilterDay.Value.Date == date)
                root.FontWeight = FontWeights.Bold;

            var stack = new StackPanel();
            var dayText = new TextBlock
            {
                Text = date.Day.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 4)
            };
            if (TodoDue.IsDatePast(date) && HasOpenDueOn(date, items))
                dayText.Foreground = TryBrush("DangerBrush", Brushes.IndianRed);
            stack.Children.Add(dayText);
            var dots = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            int shown = 0;
            for (int i = 0; i < items.Count && shown < 8; i++)
            {
                var it = items[i];
                if (!it.DueAt.HasValue || it.DueAt.Value.Date != date) continue;
                dots.Children.Add(new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Margin = new Thickness(1),
                    Fill = MarkerBrush(it)
                });
                shown++;
            }
            stack.Children.Add(dots);
            root.Content = stack;
            return root;
        }

        private static bool HasOpenDueOn(DateTime date, List<TodoItem> items)
        {
            if (items == null) return false;
            DateTime day = date.Date;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null || it.Completed || !it.DueAt.HasValue) continue;
                if (it.DueAt.Value.Date == day) return true;
            }
            return false;
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
            _calFilterDay = (DateTime)btn.Tag;
            ListHost.Visibility = Visibility.Visible;
            CalPanel.Visibility = Visibility.Collapsed;
            RefreshList();
            UpdateDayHint();
        }

        private void ClearDay_Click(object sender, RoutedEventArgs e)
        {
            _calFilterDay = null;
            UpdateDayHint();
            RefreshList();
        }

        private void UpdateDayHint()
        {
            if (_calFilterDay.HasValue)
            {
                DayFilterHint.Text = "筛选 " + _calFilterDay.Value.ToString("yyyy-MM-dd");
                ClearDayBtn.Visibility = Visibility.Visible;
            }
            else
            {
                DayFilterHint.Text = "";
                ClearDayBtn.Visibility = Visibility.Collapsed;
            }
        }

        private int CurrentTab()
        {
            return TabDone.IsChecked == true ? 1 : 0;
        }

        private void RefreshList()
        {
            string keep = null;
            var selected = TaskList.SelectedItem as TodoRow;
            if (selected != null) keep = selected.Uid;
            _rows.Clear();
            var items = TodoStore.Snapshot();
            int tab = CurrentTab();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (tab == 0 && it.Completed) continue;
                if (tab == 1 && !it.Completed) continue;
                if (_calFilterDay.HasValue)
                {
                    if (!it.DueAt.HasValue || it.DueAt.Value.Date != _calFilterDay.Value.Date)
                        continue;
                }
                _rows.Add(TodoRow.From(it));
            }
            SortRows();
            _loading = true;
            TaskList.ItemsSource = null;
            TaskList.ItemsSource = _rows;
            _loading = false;
            UpdateSortHeaders();
            FitListColumns();
            if (!string.IsNullOrEmpty(keep))
                SelectUid(keep);
            UpdateDayHint();
        }

        private void SelectUid(string uid)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Uid == uid)
                {
                    _loading = true;
                    TaskList.SelectedItem = _rows[i];
                    _loading = false;
                    return;
                }
            }
        }

        private void TaskHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null)
            {
                var d = e.OriginalSource as DependencyObject;
                while (d != null && !(d is GridViewColumnHeader))
                    d = VisualTreeHelper.GetParent(d);
                header = d as GridViewColumnHeader;
            }
            if (header == null || header.Role == GridViewColumnHeaderRole.Padding) return;
            string key = SortKeyFromHeader(header.Content as string);
            if (string.IsNullOrEmpty(key)) return;
            if (_sortKey == key)
                _sortAsc = !_sortAsc;
            else
            {
                _sortKey = key;
                _sortAsc = false;
            }
            string keep = null;
            var selected = TaskList.SelectedItem as TodoRow;
            if (selected != null) keep = selected.Uid;
            SortRows();
            _loading = true;
            TaskList.ItemsSource = null;
            TaskList.ItemsSource = _rows;
            _loading = false;
            UpdateSortHeaders();
            FitListColumns();
            if (!string.IsNullOrEmpty(keep))
                SelectUid(keep);
        }

        private void SortRows()
        {
            _rows.Sort(CompareRows);
        }

        private int CompareRows(TodoRow a, TodoRow b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int c;
            if (_sortKey == "priority")
                c = a.Priority.CompareTo(b.Priority);
            else if (_sortKey == "title")
                c = string.Compare(a.Title ?? "", b.Title ?? "", StringComparison.CurrentCultureIgnoreCase);
            else if (_sortKey == "due")
                c = CompareDue(a.DueAt, b.DueAt, _sortAsc);
            else if (_sortKey == "updated")
                c = a.UpdatedAt.CompareTo(b.UpdatedAt);
            else
                c = a.CreatedAt.CompareTo(b.CreatedAt);
            if (_sortKey != "due" && !_sortAsc)
                c = -c;
            if (c == 0)
                c = b.CreatedAt.CompareTo(a.CreatedAt);
            return c;
        }

        private static int CompareDue(DateTime? a, DateTime? b, bool asc)
        {
            if (!a.HasValue && !b.HasValue) return 0;
            if (!a.HasValue) return 1;
            if (!b.HasValue) return -1;
            int c = a.Value.CompareTo(b.Value);
            return asc ? c : -c;
        }

        private void UpdateSortHeaders()
        {
            var view = TaskList != null ? TaskList.View as GridView : null;
            if (view == null || view.Columns.Count < 7) return;
            view.Columns[1].Header = SortTitle("优先级", "priority");
            view.Columns[3].Header = SortTitle("标题", "title");
            view.Columns[4].Header = SortTitle("创建时间", "created");
            view.Columns[5].Header = SortTitle("修改时间", "updated");
            view.Columns[6].Header = SortTitle("计划完成", "due");
        }

        private static string SortKeyFromHeader(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            if (content.StartsWith("优先级", StringComparison.Ordinal)) return "priority";
            if (content.StartsWith("标题", StringComparison.Ordinal)) return "title";
            if (content.StartsWith("创建时间", StringComparison.Ordinal)) return "created";
            if (content.StartsWith("修改时间", StringComparison.Ordinal)) return "updated";
            if (content.StartsWith("计划完成", StringComparison.Ordinal)) return "due";
            return null;
        }

        private string SortTitle(string title, string key)
        {
            if (_sortKey != key) return title;
            return title + (_sortAsc ? " ↑" : " ↓");
        }

        private void TaskList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            FitListColumns();
        }

        private void FitListColumns()
        {
            if (TaskList == null) return;
            var gv = TaskList.View as GridView;
            if (gv == null || gv.Columns.Count < 9) return;
            bool doneTab = CurrentTab() == 1;
            double w = TaskList.ActualWidth - SystemParameters.VerticalScrollBarWidth - 8;
            if (w < 200) return;
            double[] min = { 52, 72, 96, 120, 118, 118, 118, doneTab ? 118 : 0, 84 };
            double need = 0;
            for (int i = 0; i < 9; i++)
                need += min[i];
            if (w < need)
            {
                double scale = w / need;
                for (int i = 0; i < 9; i++)
                    gv.Columns[i].Width = min[i] <= 0 ? 0 : Math.Max(40, Math.Floor(min[i] * scale));
                return;
            }
            double extra = w - need;
            gv.Columns[0].Width = min[0];
            gv.Columns[1].Width = min[1];
            gv.Columns[2].Width = min[2] + extra * 0.18;
            gv.Columns[3].Width = min[3] + extra * 0.42;
            gv.Columns[4].Width = min[4];
            gv.Columns[5].Width = min[5];
            gv.Columns[6].Width = min[6];
            gv.Columns[7].Width = min[7];
            gv.Columns[8].Width = min[8] + extra * 0.40;
        }

        private void TaskList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_loading || _skipListClick) return;
            var src = e.OriginalSource as DependencyObject;
            while (src != null && !(src is ListViewItem))
                src = VisualTreeHelper.GetParent(src);
            if (src == null) return;
            var row = TaskList.SelectedItem as TodoRow;
            if (row == null) return;
            OpenDrawer(row.Uid);
        }

        private void TaskList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            MenuEdit_Click(sender, e);
        }

        private void ListMenu_Opened(object sender, RoutedEventArgs e)
        {
            var row = TaskList.SelectedItem as TodoRow;
            bool has = row != null;
            bool done = has && row.Completed;
            bool locked = has && row.Locked;
            if (MenuEditItem != null)
            {
                MenuEditItem.Header = (done || locked) ? "查看" : "编辑";
                MenuEditItem.IsEnabled = has;
            }
            if (MenuDoneItem != null) MenuDoneItem.IsEnabled = has && !done && !locked;
            if (MenuTodoItem != null) MenuTodoItem.IsEnabled = has && done && !locked;
            if (MenuDeleteItem != null) MenuDeleteItem.IsEnabled = has && !locked;
        }

        private void ListMenu_Closed(object sender, RoutedEventArgs e)
        {
            SkipNextListClick();
        }

        private void SkipNextListClick()
        {
            _skipListClick = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                _skipListClick = false;
            }), DispatcherPriority.Input);
        }

        private void MenuEdit_Click(object sender, RoutedEventArgs e)
        {
            var row = TaskList.SelectedItem as TodoRow;
            if (row == null) return;
            OpenDrawer(row.Uid);
        }

        private void MenuDone_Click(object sender, RoutedEventArgs e)
        {
            var row = TaskList.SelectedItem as TodoRow;
            if (row == null) return;
            if (row.Locked) return;
            SkipNextListClick();
            if (!CloseDrawer()) return;
            TodoStore.SetCompleted(row.Uid, true);
            RefreshList();
        }

        private void MenuTodo_Click(object sender, RoutedEventArgs e)
        {
            var row = TaskList.SelectedItem as TodoRow;
            if (row == null) return;
            if (row.Locked) return;
            SkipNextListClick();
            if (!CloseDrawer()) return;
            TodoStore.SetCompleted(row.Uid, false);
            RefreshList();
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var row = TaskList.SelectedItem as TodoRow;
            if (row == null) return;
            if (row.Locked)
            {
                MsgText.Text = "已锁定，只能查看。取消勾选「锁定」后才能删除。";
                return;
            }
            if (!ConfirmHelper.Delete("任务 " + row.Id + (string.IsNullOrEmpty(row.Title) ? "" : " / " + row.Title)))
                return;
            if (DrawerHost.Visibility == Visibility.Visible && DrawerEditor.CurrentUid == row.Uid)
                CloseDrawerDiscard();
            else if (!CloseDrawer())
                return;
            TodoStore.Delete(row.Uid);
            RefreshList();
            MsgText.Text = "已删除";
        }

        private void OpenDrawer(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            if (DrawerHost.Visibility == Visibility.Visible && DrawerEditor.CurrentUid == uid)
                return;
            if (!CloseDrawer()) return;
            var item = TodoStore.GetByUid(uid);
            if (item == null) return;
            OpenDrawerItem(item, false);
        }

        private void OpenDrawerItem(TodoItem item, bool isNew)
        {
            if (item == null) return;
            DrawerEditor.LoadItem(item, isNew);
            bool completed = DrawerEditor.IsCompletedLocked;
            if (isNew)
                DrawerTitle.Text = "新建任务";
            else if (completed)
                DrawerTitle.Text = "任务详情（只读）";
            else
                DrawerTitle.Text = DrawerEditor.IsReadOnly ? "任务详情（锁定）" : "任务详情";
            _chromeLoading = true;
            try
            {
                if (ReadOnlyCheck != null)
                {
                    ReadOnlyCheck.IsChecked = DrawerEditor.IsReadOnly;
                    ReadOnlyCheck.IsEnabled = !completed && !isNew;
                    ReadOnlyCheck.Visibility = completed ? Visibility.Collapsed : Visibility.Visible;
                }
                if (AutoSaveCheck != null)
                {
                    bool auto = false;
                    try { auto = ConfigManager.Instance.Settings.TodoAutoSave; }
                    catch { }
                    AutoSaveCheck.IsChecked = auto;
                    AutoSaveCheck.Visibility = completed ? Visibility.Collapsed : Visibility.Visible;
                    DrawerEditor.SetAutoSave(auto);
                }
            }
            finally
            {
                _chromeLoading = false;
            }
            ApplyDrawerButtons(DrawerEditor.IsReadOnly);
            if (completed)
                DrawerMsg.Text = "已办任务只能查看，改回待办后才能编辑";
            else if (DrawerEditor.IsReadOnly)
                DrawerMsg.Text = "已锁定，取消勾选后可编辑";
            else
                DrawerMsg.Text = "";
            DrawerHost.Visibility = Visibility.Visible;
            SetDrawerMaximized(_drawerMax);
        }

        private void ApplyDrawerButtons(bool readOnly)
        {
            var visEdit = readOnly ? Visibility.Collapsed : Visibility.Visible;
            var visRead = readOnly ? Visibility.Visible : Visibility.Collapsed;
            if (DrawerPreviewBtn != null) DrawerPreviewBtn.Visibility = visEdit;
            if (DrawerSaveBtn != null) DrawerSaveBtn.Visibility = visEdit;
            if (DrawerSaveCloseBtn != null) DrawerSaveCloseBtn.Visibility = visEdit;
            if (DrawerCloseFooterBtn != null) DrawerCloseFooterBtn.Visibility = visRead;
        }

        private bool CloseDrawer()
        {
            if (DrawerHost.Visibility != Visibility.Visible) return true;
            if (DrawerEditor.AutoSaveEnabled)
            {
                string err;
                if (!DrawerEditor.TrySave(out err))
                {
                    if (!string.IsNullOrEmpty(err))
                    {
                        DrawerMsg.Text = err;
                        MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return false;
                }
                HideDrawer();
                RefreshList();
                return true;
            }
            if (!DrawerEditor.IsDirty())
            {
                if (DrawerEditor.IsNewItem)
                    DrawerEditor.DiscardNewFiles();
                HideDrawer();
                return true;
            }
            var r = ConfirmHelper.Unsaved("当前任务有未保存的修改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes)
            {
                string err;
                if (!DrawerEditor.TrySave(out err))
                {
                    if (!string.IsNullOrEmpty(err))
                    {
                        DrawerMsg.Text = err;
                        MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return false;
                }
                HideDrawer();
                RefreshList();
                return true;
            }
            if (DrawerEditor.IsNewItem)
                DrawerEditor.DiscardNewFiles();
            HideDrawer();
            return true;
        }

        private void CloseDrawerDiscard()
        {
            if (DrawerHost.Visibility != Visibility.Visible) return;
            DrawerEditor.DiscardNewFiles();
            HideDrawer();
        }

        private void HideDrawer()
        {
            DrawerHost.Visibility = Visibility.Collapsed;
            SetDrawerMaximized(false);
        }

        private void DrawerMax_Click(object sender, RoutedEventArgs e)
        {
            SetDrawerMaximized(!_drawerMax);
        }

        private const double DrawerRatio = 0.70;

        private void SetDrawerMaximized(bool on)
        {
            _drawerMax = on;
            ApplyDrawerSize();
            if (IconExpand != null)
                IconExpand.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (IconRestore != null)
                IconRestore.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (DrawerMaxBtn != null)
                DrawerMaxBtn.ToolTip = on ? "还原" : "最大化";
        }

        private void ApplyDrawerSize()
        {
            DrawerLayout.Apply(DrawerPanel, DrawerMask, DrawerHost, _drawerMax, DrawerRatio);
        }

        private void DrawerMask_Click(object sender, MouseButtonEventArgs e)
        {
            CloseDrawer();
        }

        private void DrawerClose_Click(object sender, RoutedEventArgs e)
        {
            CloseDrawer();
        }

        private void DrawerSave_Click(object sender, RoutedEventArgs e)
        {
            SaveDrawer(false);
        }

        private void DrawerSaveClose_Click(object sender, RoutedEventArgs e)
        {
            SaveDrawer(true);
        }

        private void SaveDrawer(bool closeAfter)
        {
            bool wasNew = DrawerEditor.IsNewItem;
            string err;
            if (!DrawerEditor.TrySave(out err))
            {
                if (!string.IsNullOrEmpty(err))
                {
                    DrawerMsg.Text = err;
                    MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }
            DrawerTitle.Text = "任务详情";
            DrawerMsg.Text = "已保存";
            if (wasNew)
                TabTodo.IsChecked = true;
            RefreshList();
            SelectUid(DrawerEditor.CurrentUid);
            if (wasNew)
            {
                var saved = TodoStore.GetByUid(DrawerEditor.CurrentUid);
                MsgText.Text = "已新建 " + (saved != null ? saved.Id : "");
                if (ReadOnlyCheck != null) ReadOnlyCheck.IsEnabled = true;
            }
            if (closeAfter)
                CloseDrawer();
        }

        private void ReadOnlyCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_chromeLoading || DrawerHost.Visibility != Visibility.Visible) return;
            DrawerEditor.SetUserLocked(ReadOnlyCheck != null && ReadOnlyCheck.IsChecked == true);
            ApplyDrawerButtons(DrawerEditor.IsReadOnly);
            if (!DrawerEditor.IsNewItem)
            {
                if (DrawerEditor.IsCompletedLocked)
                    DrawerTitle.Text = "任务详情（只读）";
                else
                    DrawerTitle.Text = DrawerEditor.IsReadOnly ? "任务详情（锁定）" : "任务详情";
            }
            if (DrawerEditor.IsCompletedLocked)
                DrawerMsg.Text = "已办任务只能查看，改回待办后才能编辑";
            else if (DrawerEditor.IsReadOnly)
                DrawerMsg.Text = "已锁定，取消勾选后可编辑";
            else
                DrawerMsg.Text = "";
            RefreshList();
            SelectUid(DrawerEditor.CurrentUid);
        }

        private void AutoSaveCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_chromeLoading) return;
            bool on = AutoSaveCheck != null && AutoSaveCheck.IsChecked == true;
            try
            {
                ConfigManager.Instance.Settings.TodoAutoSave = on;
                ConfigManager.Instance.Save();
            }
            catch { }
            DrawerEditor.SetAutoSave(on);
        }

        private void DrawerPreview_Click(object sender, RoutedEventArgs e)
        {
            var item = DrawerEditor.PeekForPreview();
            if (item == null)
            {
                DrawerMsg.Text = "请先填写任务 ID";
                return;
            }
            var w = new TodoReminderWindow(item, 0, true, DrawerEditor.PeekTitle());
            w.Show();
        }

        private void ExportXlsx_Click(object sender, RoutedEventArgs e)
        {
            var pick = new TodoExportWindow();
            pick.Owner = Window.GetWindow(this);
            pick.Prefill(CurrentTab());
            if (pick.ShowDialog() != true) return;
            var items = ItemsForExport(pick.Scope);
            if (items.Count == 0)
            {
                MessageBox.Show("没有可导出的任务。", "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var dlg = new SaveFileDialog
            {
                Filter = "Excel|*.xlsx",
                FileName = stamp + "-" + pick.FileLabel + ".xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            string err = TodoStore.ExportXlsx(dlg.FileName, items);
            MsgText.Text = err == null ? "已导出 " + items.Count + " 条" : "导出失败：" + err;
        }

        private static List<TodoItem> ItemsForExport(int scope)
        {
            var src = TodoStore.Snapshot();
            var list = new List<TodoItem>();
            for (int i = 0; i < src.Count; i++)
            {
                var it = src[i];
                if (scope == TodoExportWindow.ScopeTodo && it.Completed) continue;
                if (scope == TodoExportWindow.ScopeDone && !it.Completed) continue;
                list.Add(it);
            }
            return list;
        }

        private sealed class TodoRow
        {
            public string Uid { get; set; }
            public string Id { get; set; }
            public string Title { get; set; }
            public string StatusText { get; set; }
            public int Priority { get; set; }
            public string PriorityText { get; set; }
            public Brush PriorityBrush { get; set; }
            public DateTime CreatedAt { get; set; }
            public string CreatedText { get; set; }
            public DateTime UpdatedAt { get; set; }
            public string UpdatedText { get; set; }
            public DateTime? DueAt { get; set; }
            public string DueText { get; set; }
            public Brush DueBrush { get; set; }
            public string CompletedText { get; set; }
            public string RemindText { get; set; }
            public bool Completed { get; set; }
            public bool Locked { get; set; }

            public static TodoRow From(TodoItem it)
            {
                string plain = "";
                try
                {
                    string path = DataPaths.TodoDetailsPath(it.Uid);
                    if (System.IO.File.Exists(path))
                    {
                        using (var fs = System.IO.File.OpenRead(path))
                        {
                            var doc = System.Windows.Markup.XamlReader.Load(fs) as FlowDocument;
                            plain = TodoRichText.ToPlain(doc);
                        }
                    }
                }
                catch { }
                string title = TodoRichText.ExtractTitle(plain);
                return new TodoRow
                {
                    Uid = it.Uid,
                    Id = it.Id,
                    Title = title,
                    StatusText = it.Completed ? "已办" : "待办",
                    Priority = it.Priority,
                    PriorityText = TodoPriority.Label(it.Priority),
                    PriorityBrush = BrushOfPriority(it.Priority),
                    CreatedAt = it.CreatedAt,
                    CreatedText = it.CreatedAt == default(DateTime) ? "" : it.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    UpdatedAt = it.UpdatedAt,
                    UpdatedText = it.UpdatedAt == default(DateTime) ? "" : it.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                    DueAt = it.DueAt,
                    DueText = it.DueAt.HasValue ? it.DueAt.Value.ToString("yyyy-MM-dd HH:mm") : "",
                    DueBrush = BrushOfDue(it.DueAt),
                    CompletedText = it.CompletedAt.HasValue ? it.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm") : "",
                    RemindText = it.Completed ? "" : TodoStore.RemindSummary(it),
                    Completed = it.Completed,
                    Locked = it.ReadOnly
                };
            }

            private static Brush BrushOfDue(DateTime? due)
            {
                if (TodoDue.IsDatePast(due))
                    return ThemeBrush("DangerBrush", Brushes.IndianRed);
                return ThemeBrush("TextPrimaryBrush", Brushes.Black);
            }

            private static Brush BrushOfPriority(int p)
            {
                string key = "AccentBrush";
                if (p == TodoPriority.High) key = "DangerBrush";
                else if (p == TodoPriority.Low) key = "TextSecondaryBrush";
                return ThemeBrush(key, Brushes.Gray);
            }

            private static Brush ThemeBrush(string key, Brush fallback)
            {
                var app = Application.Current;
                if (app != null)
                {
                    var b = app.TryFindResource(key) as Brush;
                    if (b != null) return b;
                }
                return fallback;
            }
        }
    }
}
