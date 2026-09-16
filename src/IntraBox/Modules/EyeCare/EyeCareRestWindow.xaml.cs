using System;
using System.Windows;
using System.Windows.Threading;

namespace IntraBox.Modules.EyeCare
{
    public partial class EyeCareRestWindow : Window
    {
        private DispatcherTimer _auto;

        public EyeCareRestWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            double left = wa.Right - ActualWidth - 16;
            double top = wa.Bottom - ActualHeight - 16;
            if (left < wa.Left) left = wa.Left + 8;
            if (top < wa.Top) top = wa.Top + 8;
            Left = left;
            Top = top;
            _auto = new DispatcherTimer();
            _auto.Interval = TimeSpan.FromSeconds(22);
            _auto.Tick += delegate { Close(); };
            _auto.Start();
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
            base.OnClosed(e);
        }
    }
}
