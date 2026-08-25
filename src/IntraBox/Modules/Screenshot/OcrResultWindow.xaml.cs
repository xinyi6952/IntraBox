using System.Windows;
using IntraBox.Core;

namespace IntraBox.Modules.Screenshot
{
    public partial class OcrResultWindow : Window
    {
        public OcrResultWindow(string text)
        {
            InitializeComponent();
            ResultBox.Text = string.IsNullOrEmpty(text) ? "（未识别到文字）" : text;
            ResultBox.Focus();
            ResultBox.SelectAll();
            Closed += (s, e) => GcHelper.CollectSafely();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            string err;
            ClipboardHelper.TrySetText(ResultBox.Text ?? "", out err);
            if (!string.IsNullOrEmpty(err))
                MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
