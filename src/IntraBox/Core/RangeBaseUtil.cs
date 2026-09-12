using System.Windows.Controls.Primitives;

namespace IntraBox.Core
{
    /// <summary>
    /// 给 Slider 设整数上下限。Minimum/Maximum 是 double，
    /// XAML {x:Static} 绑 int 会装箱，校验按 double 拆箱失败（打开设置即崩）。
    /// 先写 Maximum 再写 Minimum，并先把 Value 压进新区间。
    /// </summary>
    public static class RangeBaseUtil
    {
        public static void SetBounds(RangeBase control, int min, int max)
        {
            if (control == null) return;
            double dMin = min;
            double dMax = max < min ? min : max;
            if (control.Value > dMax)
                control.Value = dMax;
            control.Maximum = dMax;
            control.Minimum = dMin;
            if (control.Maximum < dMax)
                control.Maximum = dMax;
            if (control.Value < dMin)
                control.Value = dMin;
            else if (control.Value > dMax)
                control.Value = dMax;
        }
    }
}
