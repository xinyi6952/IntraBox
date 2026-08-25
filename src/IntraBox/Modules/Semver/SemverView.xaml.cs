using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.Semver
{
    public partial class SemverView : UserControl, IModuleView
    {
        public SemverView() { InitializeComponent(); }
        public void OnActivated() { }
        public void OnDeactivated() { }

        private void Cmp_Click(object sender, RoutedEventArgs e)
        {
            SemverUtil.Version a, b;
            string err;
            if (!SemverUtil.TryParse(ABox.Text, out a, out err)) { Set(err, true); return; }
            if (!SemverUtil.TryParse(BBox.Text, out b, out err)) { Set(err, true); return; }
            var item = OpCombo.SelectedItem as ComboBoxItem;
            string op = item != null ? (item.Content as string) : "=";
            bool ok = SemverUtil.CompareOp(a, b, op);
            ResultText.Text = "A " + op + " B = " + (ok ? "成立" : "不成立")
                + "（比较值 " + SemverUtil.Compare(a, b) + "）";
            Set("", false);
        }

        private void Range_Click(object sender, RoutedEventArgs e)
        {
            SemverUtil.Version v;
            string err;
            if (!SemverUtil.TryParse(VBox.Text, out v, out err)) { Set(err, true); return; }
            bool ok = SemverUtil.Satisfies(v, RangeBox.Text, out err);
            if (err != null) { Set(err, true); return; }
            ResultText.Text = ok ? "满足范围" : "不满足范围";
            Set("", false);
        }

        private void Set(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }
    }
}
