using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IntraBox.Controls
{
    /// <summary>true → Collapsed，false → Visible。用于日期规则切换输入控件。</summary>
    public sealed class InverseBoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;
            return flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility && (Visibility)value == Visibility.Collapsed;
        }
    }
}
