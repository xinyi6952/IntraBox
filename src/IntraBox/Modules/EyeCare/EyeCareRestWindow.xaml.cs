using System;
using System.Windows;
using System.Windows.Threading;
using IntraBox.Core;

namespace IntraBox.Modules.EyeCare
{
    public partial class EyeCareRestWindow : Window
    {
        private static EyeCareRestWindow _open;
        private DispatcherTimer _auto;
        private readonly bool _preview;

        public EyeCareRestWindow() : this(false)
        {
        }

        public EyeCareRestWindow(bool preview)
        {
            _preview = preview;
            InitializeComponent();
            if (_preview && CaptionText != null)
                CaptionText.Text = "用眼提醒（预览）";
        }

        /// <summary>同时只显示一窗。预览与正式提醒共用，不重置 20 分钟计时。</summary>
        public static void ShowOnce(bool preview)
        {
            if (_open != null)
            {
                try { _open.Activate(); } catch { }
                return;
            }
            _open = new EyeCareRestWindow(preview);
            _open.Closed += delegate { _open = null; };
            _open.Show();
        }

        public static void CloseIfOpen()
        {
            EyeCareRestWindow w = _open;
            if (w == null) return;
            try { w.Close(); } catch { }
        }

        public static void RepositionIfOpen()
        {
            EyeCareRestWindow w = _open;
            if (w == null) return;
            try { w.PlaceAtTrayCorner(); } catch { }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PlaceAtTrayCorner();
            _auto = new DispatcherTimer();
            _auto.Interval = TimeSpan.FromSeconds(EyeCareHelper.RestPopupSeconds);
            _auto.Tick += delegate { Close(); };
            _auto.Start();
        }

        private void PlaceAtTrayCorner()
        {
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            double extra = 0;
            Window todo = FindTodoReminder();
            if (todo != null)
            {
                double h = todo.ActualHeight;
                if (h < 1) h = 200;
                extra = h + 12;
            }
            double left = wa.Right - ActualWidth - 16;
            double top = wa.Bottom - ActualHeight - 16 - extra;
            if (left < wa.Left) left = wa.Left + 8;
            if (top < wa.Top) top = wa.Top + 8;
            Left = left;
            Top = top;
        }

        private static Window FindTodoReminder()
        {
            var app = Application.Current;
            if (app == null) return null;
            foreach (Window w in app.Windows)
            {
                if (w == null || w == _open || !w.IsVisible) continue;
                if (w is IntraBox.Modules.Todo.TodoReminderWindow) return w;
            }
            return null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_auto != null)
            {
                _auto.Stop();
                _auto = null;
            }
            if (ReferenceEquals(_open, this)) _open = null;
            base.OnClosed(e);
        }
    }
}
