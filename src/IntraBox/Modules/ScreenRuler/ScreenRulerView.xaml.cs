using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.ScreenRuler
{
    public partial class ScreenRulerView : UserControl, IModuleView
    {
        public ScreenRulerView() { InitializeComponent(); }

        public void OnActivated()
        {
            var s = ConfigManager.Instance.Settings;
            uint mod = 0;
            if (s.RulerHotkeyCtrl) mod |= 0x0002;
            if (s.RulerHotkeyAlt) mod |= 0x0001;
            if (s.RulerHotkeyShift) mod |= 0x0004;
            int vk = s.RulerHotkeyVk > 0 ? s.RulerHotkeyVk : 0x4D;
            HotkeyHint.Text = "热键：" + HotkeyService.Format(mod, (uint)vk);
        }

        public void OnDeactivated() { }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            ScreenRulerOverlayWindow.ShowNew();
        }
    }
}
