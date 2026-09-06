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

        public TodoReminderWindow(TodoItem item, int remain)
            : this(item, remain, false, null)
        {
        }

        public TodoReminderWindow(TodoItem item, int remain, bool preview, string titleOverride)
        {
            InitializeComponent();
            _item = item;
            _preview = preview;
            IdText.Text = item != null ? "任务 ID  " + item.Id : "";
            string title = titleOverride;
            if (string.IsNullOrEmpty(title)) title = LoadTitle(item);
            TitleText.Text = string.IsNullOrEmpty(title) ? "" : title;
            if (item != null && item.DueAt.HasValue)
                DueText.Text = "计划完成  " + item.DueAt.Value.ToString("yyyy-MM-dd HH:mm");
            else
                DueText.Text = "未设置计划完成时间";
            if (preview)
                MoreText.Text = "这是提醒预览，不会记入已提醒记录。";
            else
                MoreText.Text = remain > 0 ? "还有 " + remain + " 条待提醒" : "";
            if (item != null && !preview && !string.IsNullOrEmpty(item.Uid))
                TodoStore.MarkReminded(item.Uid, DateTime.Now);
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
            if (!_preview && _item != null)
                TodoReminderService.Snooze(_item.Uid, TimeSpan.FromMinutes(15));
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
