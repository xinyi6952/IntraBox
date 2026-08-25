using System.Windows;
using System.Windows.Controls;

namespace IntraBox.Controls
{
    /// <summary>
    /// 模块内最大化：输出控件铺满根 Grid，其它区域隐藏。不弹新窗口。
    /// </summary>
    public static class InPlaceMaximize
    {
        public static void Apply(bool maximized, FrameworkElement output, int homeRow, int totalRows,
            params UIElement[] hideWhenMax)
        {
            Apply(maximized, output, homeRow, totalRows, -1, 1, hideWhenMax);
        }

        /// <summary>同时占满行和列（如左右分栏的预览区）。homeCol 小于 0 时不改列。</summary>
        public static void Apply(bool maximized, FrameworkElement output, int homeRow, int totalRows,
            int homeCol, int totalCols, params UIElement[] hideWhenMax)
        {
            if (output == null) return;
            if (hideWhenMax != null)
            {
                for (int i = 0; i < hideWhenMax.Length; i++)
                {
                    if (hideWhenMax[i] != null)
                        hideWhenMax[i].Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            Grid.SetRow(output, maximized ? 0 : homeRow);
            Grid.SetRowSpan(output, maximized ? totalRows : 1);
            if (homeCol >= 0)
            {
                Grid.SetColumn(output, maximized ? 0 : homeCol);
                Grid.SetColumnSpan(output, maximized ? totalCols : 1);
            }
        }
    }
}
