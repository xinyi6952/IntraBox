using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using IntraBox.Core;

namespace IntraBox.Modules.Todo
{
    public partial class TodoReminderWindow : Window
    {
        private readonly TodoItem _item;
        private readonly bool _preview;
        private readonly bool _lastFire;
        private readonly int _snoozeMin;

        public TodoReminderWindow(TodoItem item, int remain)
            : this(item, remain, false, null)
        {
        }

        public TodoReminderWindow(TodoItem item, int remain, bool preview, string titleOverride)
        {
            InitializeComponent();
            _item = item;
            _preview = preview;
            _snoozeMin = TodoRemindRepeat.ClampIntervalMin(item != null ? item.RemindIntervalMin : 0);
            if (SnoozeBtn != null)
                SnoozeBtn.Content = "稍后 " + _snoozeMin + " 分钟";
            IdText.Text = item != null ? "任务 ID  " + item.Id : "";
            string title = titleOverride;
            if (string.IsNullOrEmpty(title)) title = LoadTitle(item);
            TitleText.Text = string.IsNullOrEmpty(title) ? "" : title;
            if (item != null && item.DueAt.HasValue)
            {
                DueText.Text = "计划完成  " + item.DueAt.Value.ToString("yyyy-MM-dd HH:mm");
                if (TodoDue.IsDatePast(item.DueAt))
                {
                    var danger = TryFindResource("DangerBrush") as System.Windows.Media.Brush;
                    DueText.Foreground = danger ?? System.Windows.Media.Brushes.IndianRed;
                }
            }
            else
                DueText.Text = "未设置计划完成时间";

            int times = item != null ? TodoRemindRepeat.ClampTimes(item.RemindTimes) : 0;
            int fired = 0;
            if (item != null && !preview && !string.IsNullOrEmpty(item.Uid))
                fired = TodoStore.MarkReminded(item.Uid, DateTime.Now);
            _lastFire = !preview && times > 0 && fired >= times;

            if (RemindText != null)
            {
                if (item != null && item.RemindKind != TodoRemindKind.Off)
                {
                    string nth = preview
                        ? ("共" + times + "次")
                        : ("第" + fired + "/" + times + "次");
                    RemindText.Text = "提醒  " + item.RemindHour.ToString("D2") + ":" + item.RemindMinute.ToString("D2")
                        + " · " + nth + " / " + _snoozeMin + "分钟";
                    RemindText.Visibility = Visibility.Visible;
                }
                else
                {
                    RemindText.Text = "";
                    RemindText.Visibility = Visibility.Collapsed;
                }
            }
            if (preview)
                MoreText.Text = "这是提醒预览，不会记入已提醒记录。";
            else if (_lastFire)
                MoreText.Text = "这是今天最后一次提醒，关闭后今天不再弹出。";
            else
                MoreText.Text = remain > 0 ? "还有 " + remain + " 条待提醒" : "";
            if (_lastFire)
            {
                if (StopBtn != null) StopBtn.Visibility = Visibility.Collapsed;
                if (SnoozeBtn != null) SnoozeBtn.Visibility = Visibility.Collapsed;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PlaceAtTrayCorner();
            StartFlash();
        }

        private void PlaceAtTrayCorner()
        {
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            double left = wa.Right - ActualWidth - 16;
            double top = wa.Bottom - ActualHeight - 16;
            if (left < wa.Left) left = wa.Left + 8;
            if (top < wa.Top) top = wa.Top + 8;
            Left = left;
            Top = top;
        }

        private void StartFlash()
        {
            if (FlashOverlay == null) return;
            FlashOverlay.BeginAnimation(OpacityProperty, null);
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 0.18,
                Duration = TimeSpan.FromMilliseconds(620),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(4),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            anim.Completed += (s, e) =>
            {
                FlashOverlay.BeginAnimation(OpacityProperty, null);
                FlashOverlay.Opacity = 0;
            };
            FlashOverlay.BeginAnimation(OpacityProperty, anim);
        }

        private void StopFlash()
        {
            if (FlashOverlay == null) return;
            FlashOverlay.BeginAnimation(OpacityProperty, null);
            FlashOverlay.Opacity = 0;
        }

        private static string LoadTitle(TodoItem item)
        {
            if (item == null) return "";
            try
            {
                string path = DataPaths.TodoDetailsPath(item.Uid);
                if (!File.Exists(path)) return item.Id;
                using (var fs = File.OpenRead(path))
                {
                    var doc = XamlReader.Load(fs) as FlowDocument;
                    string t = TodoRichText.ExtractTitle(TodoRichText.ToPlain(doc));
                    return string.IsNullOrEmpty(t) ? item.Id : t;
                }
            }
            catch
            {
                return item.Id;
            }
        }

        private void Snooze_Click(object sender, RoutedEventArgs e)
        {
            if (!_preview && !_lastFire && _item != null)
                TodoReminderService.Snooze(_item.Uid, TimeSpan.FromMinutes(_snoozeMin));
            Close();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_item == null || string.IsNullOrEmpty(_item.Uid))
            {
                Close();
                return;
            }
            if (!ConfirmHelper.WarnOverTopmost(this,
                "今日内本任务将不再弹出提醒，明天起仍按原频次提醒。\n\n确定今日不再提醒？",
                "今日不再提醒"))
                return;
            if (!_preview)
                TodoReminderService.MuteToday(_item.Uid);
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopFlash();
            base.OnClosed(e);
        }
    }
}
