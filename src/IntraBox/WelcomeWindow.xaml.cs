using System.Windows;
using IntraBox.Core;

namespace IntraBox
{
    public partial class WelcomeWindow : Window
    {
        public WelcomeWindow()
        {
            InitializeComponent();
            VersionText.Text = "版本 " + AppVersion.Display;
            VersionText.ToolTip = AppVersion.Full;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
