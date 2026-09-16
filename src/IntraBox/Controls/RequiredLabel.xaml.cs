using System.Windows;
using System.Windows.Controls;

namespace IntraBox.Controls
{
    /// <summary>必填项标签：红色 * + 名称。</summary>
    public partial class RequiredLabel : UserControl
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            "Text", typeof(string), typeof(RequiredLabel),
            new PropertyMetadata("", OnTextChanged));

        public RequiredLabel()
        {
            InitializeComponent();
        }

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = d as RequiredLabel;
            if (ctl == null || ctl.CaptionBlock == null) return;
            ctl.CaptionBlock.Text = e.NewValue as string ?? "";
        }
    }
}
