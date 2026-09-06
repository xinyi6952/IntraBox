using System.Windows;

namespace IntraBox
{
    /// <summary>
    /// 主窗口关闭询问：最小化到托盘或退出。托盘右键退出不走此窗。
    /// DialogResult=true 表示确认；ChoseExit 表示选了退出。
    /// </summary>
    public partial class ClosePromptWindow : Window
    {
        public bool ChoseExit { get; private set; }

        public bool DontAskAgain
        {
            get { return DontAskCheck != null && DontAskCheck.IsChecked == true; }
        }

        public ClosePromptWindow()
        {
            InitializeComponent();
        }

        /// <summary>按上次偏好预选单选项（未勾选「不再提醒」时也可沿用）。</summary>
        public void PrefillPreferExit(bool preferExit)
        {
            if (preferExit)
                ExitRadio.IsChecked = true;
            else
                TrayRadio.IsChecked = true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ChoseExit = ExitRadio.IsChecked == true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TitleClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
