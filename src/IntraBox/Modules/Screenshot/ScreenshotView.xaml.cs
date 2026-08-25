using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.Screenshot
{
    /// <summary>截屏模块页：说明热键并提供手动入口。真正截屏是全局覆盖层，按需创建、用完销毁。</summary>
    public partial class ScreenshotView : UserControl, IModuleView
    {
        public ScreenshotView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            bool ok = HotkeyService.Instance != null && HotkeyService.Instance.Registered;
            HotkeyHint.Text = ok
                ? "全局热键已注册：" + HotkeyService.Instance.DisplayText
                : "热键注册失败（可能被占用），请用按钮开始截屏。";
            MsgText.Text = "";
        }

        public void OnDeactivated()
        {
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            CaptureOverlayWindow.ShowNew();
        }
    }
}
