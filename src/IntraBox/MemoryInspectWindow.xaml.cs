using System.Windows;
using IntraBox.Core;
using IntraBox.Modules.ClipboardHistory;

namespace IntraBox
{
    public partial class MemoryInspectWindow : Window
    {
        private string _toolName;

        public MemoryInspectWindow()
        {
            InitializeComponent();
        }

        public void LoadFrom(string toolName)
        {
            _toolName = toolName;
            Apply(MemoryUsage.Capture());
            MsgText.Text = "";
        }

        private void Apply(MemoryUsage u)
        {
            if (u == null) u = MemoryUsage.Capture();
            WorkingText.Text = u.WorkingSetText;
            PrivateText.Text = u.PrivateText;
            ManagedText.Text = u.ManagedText;
            ToolText.Text = string.IsNullOrEmpty(_toolName) ? "（未打开工具）" : _toolName;
            int n = 0;
            try { n = ClipboardStore.Items.Count; }
            catch { }
            ClipText.Text = n + " 条（图片会保留原图像素）";
        }

        private void Trim_Click(object sender, RoutedEventArgs e)
        {
            MemoryUsage after;
            if (!GcHelper.TryCollectAndTrim(out after, "manual"))
            {
                MsgText.Text = "刚回收过，请稍后再试";
                return;
            }
            Apply(after);
            MsgText.Text = "已回收，工作集 " + after.WorkingSetText;
            var main = Owner as MainWindow;
            if (main != null) main.RefreshMemoryText();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
