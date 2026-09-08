using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.Todo
{
    public partial class TodoEditorPanel : UserControl
    {
        private static readonly Regex AutoIdPattern = new Regex(@"^T-\d{8}-\d+$", RegexOptions.CultureInvariant);
        private static readonly double[] FontSizes = { 10, 12, 14, 16, 18, 20, 24, 28, 32 };

        private bool _loading;
        private bool _isNew;
        private TodoItem _item;
        private string _originalId = "";
        private DateTime? _loadedDue;
        private string _fingerprint = "";
        private bool _completedLocked;
        private bool _userLocked;
        private bool _autoSaveChecked;
        private bool _allowBrokenFormat;
        private DispatcherTimer _saveTimer;

        public event EventHandler Saved;

        public TodoEditorPanel()
        {
            InitializeComponent();
            _loading = true;
            FillTimeCombos(DueHourCombo, DueMinuteCombo, 15, 0);
            FillTimeCombos(RemindHourCombo, RemindMinuteCombo, 15, 0);
            FillRemindRepeatCombos();
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
            TodoRichText.HookEditorClicks(DetailBox);
            _saveTimer = new DispatcherTimer();
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                string err;
                TrySave(out err, false);
            };
            Unloaded += (s, e) =>
            {
                if (_saveTimer != null) _saveTimer.Stop();
            };
            _loading = false;
        }

        public string CurrentUid
        {
            get { return _item != null ? _item.Uid : null; }
        }

        public bool IsNewItem
        {
            get { return _isNew; }
        }

        public bool IsReadOnly
        {
            get { return _completedLocked || _userLocked; }
        }

        public bool IsCompletedLocked
        {
            get { return _completedLocked; }
        }

        public bool AutoSaveEnabled
        {
            get { return _autoSaveChecked && !IsReadOnly; }
        }

        public void SetAutoSave(bool on)
        {
            _autoSaveChecked = on;
            if (on && !IsReadOnly && IsDirty())
                KickSave();
            else if (_saveTimer != null && !on)
                _saveTimer.Stop();
        }

        public void SetUserLocked(bool on)
        {
            if (_completedLocked || _item == null) return;
            if (_isNew && on) return;
            if (on && !_userLocked)
            {
                string err;
                TrySave(out err);
            }
            _userLocked = on;
            _item.ReadOnly = on;
            if (!_isNew)
                TodoStore.SetReadOnly(_item.Uid, on);
            ApplyReadOnly();
            if (IsReadOnly)
            {
                if (_saveTimer != null) _saveTimer.Stop();
            }
            else if (_autoSaveChecked && IsDirty())
                KickSave();
        }

        public void FlushNow()
        {
            if (_saveTimer != null) _saveTimer.Stop();
            if (_completedLocked) return;
            string err;
            TrySave(out err, true);
        }

        public void LoadItem(TodoItem item, bool isNew)
        {
            if (item == null) return;
            _loading = true;
            _isNew = isNew;
            _item = item;
            _allowBrokenFormat = false;
            _originalId = item.Id ?? "";
            _loadedDue = item.DueAt;
            _completedLocked = item.Completed && !isNew;
            _userLocked = !isNew && !item.Completed && item.ReadOnly;
            DetailBox.Tag = item.Uid;
            bool auto = isNew || AutoIdPattern.IsMatch(item.Id ?? "");
            AutoIdCheck.IsChecked = auto;
            IdBox.Text = item.Id ?? "";
            IdBox.IsReadOnly = auto;
            UpdateIdHint();
            if (item.CreatedAt != default(DateTime))
                CreatedAtText.Text = "创建时间  " + item.CreatedAt.ToString("yyyy-MM-dd HH:mm");
            else
                CreatedAtText.Text = "";
            if (item.UpdatedAt != default(DateTime))
                UpdatedAtText.Text = "修改时间  " + item.UpdatedAt.ToString("yyyy-MM-dd HH:mm");
            else
                UpdatedAtText.Text = "";
            if (item.Completed && item.CompletedAt.HasValue)
            {
                CompletedAtText.Text = "完成时间  " + item.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm");
                CompletedAtText.Visibility = Visibility.Visible;
            }
            else
            {
                CompletedAtText.Text = "";
                CompletedAtText.Visibility = Visibility.Collapsed;
            }
            PriorityCombo.SelectedIndex = item.Priority < 0 || item.Priority > 2 ? 1 : item.Priority;
            if (item.DueAt.HasValue)
            {
                SetDueDate(item.DueAt.Value.Date);
                SelectTime(DueHourCombo, DueMinuteCombo, item.DueAt.Value.Hour, item.DueAt.Value.Minute);
            }
            else
            {
                SetDueDate(null);
                SelectTime(DueHourCombo, DueMinuteCombo, 15, 0);
            }
            RemindKindCombo.SelectedIndex = item.RemindKind < 0 || item.RemindKind > 5 ? 0 : item.RemindKind;
            SelectTime(RemindHourCombo, RemindMinuteCombo, item.RemindHour, item.RemindMinute);
            SelectComboInt(RemindTimesCombo, TodoRemindRepeat.ClampTimes(item.RemindTimes));
            SelectComboInt(RemindIntervalCombo, TodoRemindRepeat.ClampIntervalMin(item.RemindIntervalMin));
            RemindWeekCombo.SelectedIndex = item.RemindWeekday;
            RemindNBox.Text = item.RemindNDays < 2 ? "2" : item.RemindNDays.ToString();
            RemindPanel.Visibility = item.Completed ? Visibility.Collapsed : Visibility.Visible;
            UpdateRemindExtra();
            if (isNew)
                DetailBox.Document = TodoRichText.CreateTemplateDocument();
            else
                TodoRichText.LoadInto(DetailBox, item.Uid);
            RefreshDueBlackout();
            UpdateDueHint();
            ApplyReadOnly();
            if (_saveTimer != null) _saveTimer.Stop();
            _loading = false;
            _fingerprint = CurrentFingerprint();
            Dispatcher.BeginInvoke(new Action(delegate
            {
                _fingerprint = CurrentFingerprint();
            }), DispatcherPriority.Loaded);
        }

        public bool IsDirty()
        {
            if (_item == null || _loading || _completedLocked) return false;
            return CurrentFingerprint() != _fingerprint;
        }

        private void KickSave()
        {
            if (_loading || _item == null || IsReadOnly || !_autoSaveChecked) return;
            int ms = AppSettings.CurrentHistoryPersistDelayMs();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(ms);
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private string CurrentFingerprint()
        {
            var sb = new StringBuilder();
            sb.Append(AutoIdCheck != null && AutoIdCheck.IsChecked == true ? "1" : "0");
            sb.Append('|');
            sb.Append(IdBox != null ? IdBox.Text ?? "" : "");
            sb.Append('|');
            sb.Append(PriorityCombo != null ? PriorityCombo.SelectedIndex : -1);
            sb.Append('|');
            var due = ParseDue();
            sb.Append(due.HasValue ? due.Value.ToString("yyyyMMddHHmm") : "");
            sb.Append('|');
            sb.Append(RemindKindCombo != null ? RemindKindCombo.SelectedIndex : -1);
            sb.Append('|');
            sb.Append(ComboInt(RemindHourCombo, 15));
            sb.Append('|');
            sb.Append(ComboInt(RemindMinuteCombo, 0));
            sb.Append('|');
            sb.Append(RemindWeekCombo != null ? RemindWeekCombo.SelectedIndex : -1);
            sb.Append('|');
            sb.Append(RemindNBox != null ? RemindNBox.Text ?? "" : "");
            sb.Append('|');
            sb.Append(ComboInt(RemindTimesCombo, TodoRemindRepeat.DefaultTimes));
            sb.Append('|');
            sb.Append(ComboInt(RemindIntervalCombo, TodoRemindRepeat.DefaultIntervalMin));
            sb.Append('|');
            sb.Append(TodoRichText.ContentStamp(DetailBox != null ? DetailBox.Document : null));
            return sb.ToString();
        }

        public bool TrySave(out string error)
        {
            return TrySave(out error, true);
        }

        public bool TrySave(out string error, bool interactive)
        {
            error = null;
            if (_completedLocked) return true;
            TodoItem collected;
            if (!TryCollect(out collected, out error)) return false;
            int format;
            if (!ConfirmDetailFormat(interactive, out format))
            {
                if (!interactive && format == 0)
                    return true;
                return false;
            }
            ApplyTrySaveRemind(_item, collected);
            DirectoryEnsure();
            TodoRichText.SaveFrom(DetailBox, collected.Uid);
            if (_isNew)
            {
                TodoStore.Insert(collected);
                _isNew = false;
            }
            else
            {
                TodoStore.Upsert(collected);
            }
            _item = collected;
            _originalId = collected.Id ?? "";
            _loadedDue = collected.DueAt;
            if (UpdatedAtText != null)
            {
                UpdatedAtText.Text = collected.UpdatedAt == default(DateTime)
                    ? ""
                    : "修改时间  " + collected.UpdatedAt.ToString("yyyy-MM-dd HH:mm");
            }
            _fingerprint = CurrentFingerprint();
            if (Saved != null) Saved(this, EventArgs.Empty);
            return true;
        }

        /// <returns>false 表示不要写入。format=0 表示模板损坏。</returns>
        private bool ConfirmDetailFormat(bool interactive, out int format)
        {
            format = 1;
            string plain = TodoRichText.ToPlain(DetailBox != null ? DetailBox.Document : null);
            if (TodoRichText.HasFieldTemplate(plain))
            {
                _allowBrokenFormat = false;
                return true;
            }
            format = 0;
            if (!interactive)
                return _allowBrokenFormat;
            if (_allowBrokenFormat) return true;
            if (!ConfirmHelper.Warn(TodoRichText.FormatBrokenMessage, "格式错误"))
                return false;
            _allowBrokenFormat = true;
            return true;
        }

        private void ApplyReadOnly()
        {
            bool edit = !IsReadOnly;
            if (AutoIdCheck != null) AutoIdCheck.IsEnabled = edit;
            if (IdBox != null)
                IdBox.IsReadOnly = !edit || (AutoIdCheck != null && AutoIdCheck.IsChecked == true);
            if (PriorityCombo != null) PriorityCombo.IsEnabled = edit;
            if (DueDate != null) DueDate.IsEnabled = edit;
            if (DueHourCombo != null) DueHourCombo.IsEnabled = edit;
            if (DueMinuteCombo != null) DueMinuteCombo.IsEnabled = edit;
            if (RemindPanel != null) RemindPanel.IsEnabled = edit;
            RefreshClearDueEnabled();
            if (RteToolbar != null) RteToolbar.IsEnabled = edit;
            if (DetailBox != null)
            {
                DetailBox.IsReadOnly = !edit;
                DetailBox.IsDocumentEnabled = false;
                DetailBox.AllowDrop = edit;
            }
        }

        public TodoItem PeekForPreview()
        {
            TodoItem collected;
            string err;
            if (!TryCollect(out collected, out err, true)) return null;
            return collected;
        }

        public string PeekTitle()
        {
            return TodoRichText.ExtractTitle(TodoRichText.ToPlain(DetailBox.Document));
        }

        public void DiscardNewFiles()
        {
            if (!_isNew || _item == null) return;
            try
            {
                string dir = DataPaths.TodoItemDir(_item.Uid);
                if (System.IO.Directory.Exists(dir))
                    System.IO.Directory.Delete(dir, true);
            }
            catch { }
        }

        private bool TryCollect(out TodoItem collected, out string error, bool skipDueDayRequired = false)
        {
            collected = null;
            error = null;
            if (_item == null)
            {
                error = "没有正在编辑的任务";
                return false;
            }
            bool auto = AutoIdCheck.IsChecked == true;
            string id = (IdBox.Text ?? "").Trim();
            if (auto)
            {
                if (string.IsNullOrEmpty(id) || !AutoIdPattern.IsMatch(id))
                    id = !string.IsNullOrEmpty(_originalId) && AutoIdPattern.IsMatch(_originalId)
                        ? _originalId
                        : TodoStore.NextAutoId();
                if (TodoStore.IdExists(id, _item.Uid))
                {
                    if (!string.IsNullOrEmpty(_originalId) && !TodoStore.IdExists(_originalId, _item.Uid))
                        id = _originalId;
                    else
                        id = TodoStore.NextAutoId();
                }
            }
            if (string.IsNullOrEmpty(id))
            {
                error = "请填写任务 ID，可填写内网工单号或其它系统中的任务编号。";
                return false;
            }
            if (TodoStore.IdExists(id, _item.Uid))
            {
                error = "任务 ID 已存在，请更换编号。";
                return false;
            }
            DateTime? due = ParseDue();
            int remindKind = RemindKindCombo != null && RemindKindCombo.SelectedIndex >= 0
                ? RemindKindCombo.SelectedIndex : 0;
            if (!skipDueDayRequired && TodoDue.DueDayNeedsDue(remindKind, due))
            {
                error = TodoDue.DueDayMissingMessage;
                return false;
            }
            if (!IsDueAllowed(due))
            {
                error = "计划完成时间不能早于当前时间";
                return false;
            }
            collected = TodoStore.GetByUid(_item.Uid) ?? CloneMeta(_item);
            collected.Uid = _item.Uid;
            collected.Id = id;
            collected.Priority = PriorityCombo.SelectedIndex < 0 ? 1 : PriorityCombo.SelectedIndex;
            collected.DueAt = due;
            collected.Completed = _item.Completed;
            collected.CompletedAt = _item.CompletedAt;
            collected.ReadOnly = _item.ReadOnly;
            collected.CreatedAt = _item.CreatedAt;
            if (!collected.Completed)
            {
                collected.RemindKind = RemindKindCombo.SelectedIndex < 0 ? 0 : RemindKindCombo.SelectedIndex;
                collected.RemindHour = ComboInt(RemindHourCombo, 15);
                collected.RemindMinute = ComboInt(RemindMinuteCombo, 0);
                collected.RemindWeekday = RemindWeekCombo.SelectedIndex < 0 ? 1 : RemindWeekCombo.SelectedIndex;
                int n;
                if (!int.TryParse(RemindNBox.Text, out n) || n < 2) n = 2;
                collected.RemindNDays = n;
                collected.RemindTimes = ComboInt(RemindTimesCombo, TodoRemindRepeat.DefaultTimes);
                collected.RemindIntervalMin = ComboInt(RemindIntervalCombo, TodoRemindRepeat.DefaultIntervalMin);
                ApplyTryCollectRemind(_item, collected);
            }
            return true;
        }

        /// <summary>TryCollect：提醒规则变了则清空已提醒，使新时刻能再弹。</summary>
        public static void ApplyTryCollectRemind(TodoItem before, TodoItem collected)
        {
            if (collected == null || collected.Completed) return;
            if (!RemindScheduleChanged(before, collected)) return;
            collected.LastRemindedAt = null;
            collected.MuteRemindOn = null;
        }

        /// <summary>TrySave：提醒规则变了则清稍后与排队。</summary>
        public static void ApplyTrySaveRemind(TodoItem before, TodoItem collected)
        {
            if (collected == null || collected.Completed) return;
            if (!RemindScheduleChanged(before, collected)) return;
            TodoReminderService.ResetLiveRemind(collected.Uid);
        }

        private bool IsDueAllowed(DateTime? due)
        {
            if (!due.HasValue) return true;
            if (due.Value >= DateTime.Now) return true;
            if (_isNew || !_loadedDue.HasValue) return false;
            return TruncMinute(due.Value) == TruncMinute(_loadedDue.Value);
        }

        private static DateTime TruncMinute(DateTime d)
        {
            return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
        }

        private static TodoItem CloneMeta(TodoItem s)
        {
            return new TodoItem
            {
                Uid = s.Uid,
                Id = s.Id,
                Priority = s.Priority,
                DueAt = s.DueAt,
                Completed = s.Completed,
                RemindKind = s.RemindKind,
                RemindHour = s.RemindHour,
                RemindMinute = s.RemindMinute,
                RemindNDays = s.RemindNDays,
                RemindWeekday = s.RemindWeekday,
                RemindTimes = s.RemindTimes,
                RemindIntervalMin = s.RemindIntervalMin,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
                LastRemindedAt = s.LastRemindedAt,
                MuteRemindOn = s.MuteRemindOn,
                CompletedAt = s.CompletedAt,
                ReadOnly = s.ReadOnly
            };
        }

        private void DirectoryEnsure()
        {
            if (_item == null) return;
            System.IO.Directory.CreateDirectory(DataPaths.TodoItemDir(_item.Uid));
        }

        private DateTime? ParseDue()
        {
            if (DueDate.SelectedDate == null) return null;
            var d = DueDate.SelectedDate.Value.Date;
            int h = ComboInt(DueHourCombo, 15);
            int m = ComboInt(DueMinuteCombo, 0);
            return new DateTime(d.Year, d.Month, d.Day, h, m, 0);
        }

        private static int ComboInt(ComboBox box, int fallback)
        {
            if (box == null || box.SelectedItem == null) return fallback;
            int n;
            if (int.TryParse(box.SelectedItem.ToString(), out n)) return n;
            return fallback;
        }

        private static void FillTimeCombos(ComboBox hour, ComboBox minute, int h, int m)
        {
            hour.Items.Clear();
            minute.Items.Clear();
            for (int i = 0; i < 24; i++)
                hour.Items.Add(i.ToString("00"));
            for (int i = 0; i < 60; i++)
                minute.Items.Add(i.ToString("00"));
            SelectTime(hour, minute, h, m);
        }

        private static void SelectTime(ComboBox hour, ComboBox minute, int h, int m)
        {
            if (h < 0 || h > 23) h = 15;
            if (m < 0 || m > 59) m = 0;
            hour.SelectedIndex = h;
            minute.SelectedIndex = m;
        }

        private void AutoId_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || AutoIdCheck == null || _item == null) return;
            bool auto = AutoIdCheck.IsChecked == true;
            IdBox.IsReadOnly = auto;
            if (auto)
            {
                if (!string.IsNullOrEmpty(_originalId))
                    IdBox.Text = _originalId;
                else
                    IdBox.Text = TodoStore.NextAutoId();
            }
            else
            {
                IdBox.Text = "";
                IdBox.Focus();
            }
            UpdateIdHint();
        }

        private void IdBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateIdHint();
            if (!_loading) KickSave();
        }

        private void UpdateIdHint()
        {
            if (IdHint == null || IdBox == null) return;
            bool show = AutoIdCheck.IsChecked != true && string.IsNullOrEmpty(IdBox.Text);
            IdHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearDue_Click(object sender, RoutedEventArgs e)
        {
            SetDueDate(null);
            SelectTime(DueHourCombo, DueMinuteCombo, 15, 0);
            UpdateDueHint();
            KickSave();
        }

        private void DueDate_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (DueDate.SelectedDate.HasValue && DueDate.SelectedDate.Value.Date < DateTime.Today)
            {
                _loading = true;
                SetDueDate(DateTime.Today);
                _loading = false;
            }
            ClampDueTime();
            RefreshDueBlackout();
            UpdateDueHint();
            KickSave();
        }

        private void DueTime_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            ClampDueTime();
            UpdateDueHint();
            KickSave();
        }

        private void ClampDueTime()
        {
            if (DueDate.SelectedDate == null) return;
            DateTime? due = ParseDue();
            if (due == null || due.Value >= DateTime.Now) return;
            _loading = true;
            DateTime n = DateTime.Now.AddMinutes(1);
            SetDueDate(n.Date);
            SelectTime(DueHourCombo, DueMinuteCombo, n.Hour, n.Minute);
            _loading = false;
        }

        private void SetDueDate(DateTime? date)
        {
            if (DueDate == null) return;
            try
            {
                DueDate.BlackoutDates.Clear();
            }
            catch { }
            try
            {
                DueDate.SelectedDate = date;
            }
            catch
            {
                try { DueDate.SelectedDate = null; } catch { }
                try { DueDate.SelectedDate = date; } catch { }
            }
        }

        private void RefreshDueBlackout()
        {
            if (DueDate == null) return;
            try
            {
                DueDate.BlackoutDates.Clear();
            }
            catch { }
            bool overdue = DueDate.SelectedDate.HasValue
                && DueDate.SelectedDate.Value.Date < DateTime.Today;
            if (overdue) return;
            try
            {
                DueDate.BlackoutDates.AddDatesInPast();
            }
            catch { }
        }

        private void UpdateDueHint()
        {
            DateTime? due = ParseDue();
            bool past = TodoDue.IsDatePast(due);
            int kind = RemindKindCombo != null ? RemindKindCombo.SelectedIndex : 0;
            if (DueHint != null)
            {
                if (TodoDue.DueDayNeedsDue(kind, due))
                {
                    DueHint.Text = TodoDue.DueDayMissingMessage;
                    DueHint.Visibility = Visibility.Visible;
                }
                else if (!IsDueAllowed(due))
                {
                    DueHint.Text = "计划完成时间不能早于当前时间";
                    DueHint.Visibility = Visibility.Visible;
                }
                else if (past)
                {
                    DueHint.Text = "计划完成日期已早于今天";
                    DueHint.Visibility = Visibility.Visible;
                }
                else
                    DueHint.Visibility = Visibility.Collapsed;
            }
            ApplyDueForeground(past);
        }

        private void ApplyDueForeground(bool overdue)
        {
            var brush = overdue
                ? (TryFindResource("DangerBrush") as Brush)
                : (TryFindResource("TextPrimaryBrush") as Brush);
            if (brush == null)
                brush = overdue ? Brushes.IndianRed : Brushes.Black;
            if (DueDate != null) DueDate.Foreground = brush;
            if (DueHourCombo != null) DueHourCombo.Foreground = brush;
            if (DueMinuteCombo != null) DueMinuteCombo.Foreground = brush;
        }

        private void RemindKind_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            UpdateRemindExtra();
            UpdateDueHint();
            KickSave();
        }

        private void Field_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            KickSave();
        }

        private void Field_Changed(object sender, TextChangedEventArgs e)
        {
            Field_Changed(sender, (RoutedEventArgs)e);
        }

        private void DetailBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            KickSave();
        }

        private void UpdateRemindExtra()
        {
            if (RemindKindCombo == null) return;
            int k = RemindKindCombo.SelectedIndex;
            RemindWeekCombo.Visibility = k == TodoRemindKind.Weekly ? Visibility.Visible : Visibility.Collapsed;
            RemindNBox.Visibility = k == TodoRemindKind.EveryNDays ? Visibility.Visible : Visibility.Collapsed;
            RemindExtraLabel.Text = k == TodoRemindKind.Weekly ? "星期" : (k == TodoRemindKind.EveryNDays ? "间隔天数" : "");
            if (RemindRepeatPanel != null)
                RemindRepeatPanel.Visibility = k == TodoRemindKind.Off ? Visibility.Collapsed : Visibility.Visible;
            RefreshClearDueEnabled();
        }

        private void RefreshClearDueEnabled()
        {
            if (ClearDueBtn == null) return;
            bool dueDay = RemindKindCombo != null && RemindKindCombo.SelectedIndex == TodoRemindKind.DueDay;
            ClearDueBtn.IsEnabled = !IsReadOnly && !dueDay;
        }

        private static bool RemindScheduleChanged(TodoItem a, TodoItem b)
        {
            if (a == null || b == null) return true;
            return a.RemindKind != b.RemindKind
                || a.RemindHour != b.RemindHour
                || a.RemindMinute != b.RemindMinute
                || a.RemindWeekday != b.RemindWeekday
                || a.RemindNDays != b.RemindNDays
                || TodoRemindRepeat.ClampTimes(a.RemindTimes) != TodoRemindRepeat.ClampTimes(b.RemindTimes)
                || TodoRemindRepeat.ClampIntervalMin(a.RemindIntervalMin) != TodoRemindRepeat.ClampIntervalMin(b.RemindIntervalMin);
        }

        private void FillRemindRepeatCombos()
        {
            if (RemindTimesCombo != null && RemindTimesCombo.Items.Count == 0)
            {
                for (int i = 1; i <= 5; i++)
                    RemindTimesCombo.Items.Add(i.ToString());
                RemindTimesCombo.SelectedIndex = TodoRemindRepeat.DefaultTimes - 1;
            }
            if (RemindIntervalCombo != null && RemindIntervalCombo.Items.Count == 0)
            {
                int[] mins = { 5, 10, 15, 30 };
                for (int i = 0; i < mins.Length; i++)
                    RemindIntervalCombo.Items.Add(mins[i].ToString());
                SelectComboInt(RemindIntervalCombo, TodoRemindRepeat.DefaultIntervalMin);
            }
        }

        private static void SelectComboInt(ComboBox box, int value)
        {
            if (box == null) return;
            for (int i = 0; i < box.Items.Count; i++)
            {
                int n;
                if (int.TryParse(box.Items[i].ToString(), out n) && n == value)
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private void Font_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || FontCombo.SelectedItem == null || DetailBox == null) return;
            DetailBox.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,
                new FontFamily(MapFontName(FontCombo.SelectedItem.ToString())));
        }

        private void Size_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || SizeCombo.SelectedItem == null || DetailBox == null) return;
            double sz;
            if (!double.TryParse(SizeCombo.SelectedItem.ToString(), out sz)) return;
            DetailBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, sz);
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (DetailBox == null) return;
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
            DetailBox.Focus();
        }

        private void DetailBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || FontCombo == null || SizeCombo == null) return;
            _loading = true;
            try
            {
                object ff = DetailBox.Selection.GetPropertyValue(TextElement.FontFamilyProperty);
                if (ff is FontFamily)
                {
                    string name = ((FontFamily)ff).Source;
                    for (int i = 0; i < FontCombo.Items.Count; i++)
                    {
                        if (string.Equals(FontCombo.Items[i].ToString(), name, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(MapFontName(FontCombo.Items[i].ToString()), name, StringComparison.OrdinalIgnoreCase))
                        {
                            FontCombo.SelectedIndex = i;
                            break;
                        }
                    }
                }
                object sz = DetailBox.Selection.GetPropertyValue(TextElement.FontSizeProperty);
                if (sz is double)
                {
                    string s = ((double)sz).ToString("0");
                    for (int i = 0; i < SizeCombo.Items.Count; i++)
                    {
                        if (SizeCombo.Items[i].ToString() == s)
                        {
                            SizeCombo.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _loading = false;
            }
        }

        private static string MapFontName(string display)
        {
            if (display == "微软雅黑") return "Microsoft YaHei";
            if (display == "宋体") return "SimSun";
            if (display == "黑体") return "SimHei";
            if (display == "楷体") return "KaiTi";
            return display;
        }

        private void InsertImage_Click(object sender, RoutedEventArgs e)
        {
            if (_item == null || IsReadOnly) return;
            var dlg = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有文件|*.*" };
            if (dlg.ShowDialog() != true) return;
            InsertImageFile(dlg.FileName);
        }

        private void DetailBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (IsReadOnly) return;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void DetailBox_Drop(object sender, DragEventArgs e)
        {
            if (IsReadOnly) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            for (int i = 0; i < files.Length; i++)
                InsertImageFile(files[i]);
            e.Handled = true;
        }

        private void InsertImageFile(string path)
        {
            if (_item == null || IsReadOnly) return;
            DirectoryEnsure();
            string err;
            if (!TodoRichText.TryInsertImage(DetailBox, _item.Uid, path, out err))
                MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
