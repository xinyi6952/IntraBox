using System;
using System.Windows.Controls;
using System.Windows.Media;

namespace IntraBox.Core
{
    /// <summary>表单校验与失败提示用红字；「已…」成功用 OkBrush；其余说明用次要色。</summary>
    public static class FormHint
    {
        private static readonly string[] ErrorKeys =
        {
            "失败", "错误", "请填写", "请输入", "请先", "请选择", "请至少", "请选",
            "不能为空", "不能保存", "不能改", "无法", "非法", "至少", "必须", "未填写", "未选择",
            "不存在", "必填", "锁定", "超出", "拒绝", "无效",
            "过大", "没有可", "不一致", "应为", "未定义变量", "只能改"
        };

        public static bool IsOk(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            if (t.StartsWith("当前：已阻止", StringComparison.Ordinal)) return true;
            if (t == "成功" || t == "通过") return true;
            if (!t.StartsWith("已", StringComparison.Ordinal)) return false;
            return t.IndexOf("失败", StringComparison.Ordinal) < 0
                && t.IndexOf("错误", StringComparison.Ordinal) < 0
                && t.IndexOf("不能", StringComparison.Ordinal) < 0
                && t.IndexOf("锁定", StringComparison.Ordinal) < 0;
        }

        public static bool IsError(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (IsOk(text)) return false;
            string t = text.Trim();
            // 操作说明里可能带「不能保存」，不算拦截
            if (t.StartsWith("填写名称和值后保存", StringComparison.Ordinal)) return false;
            for (int i = 0; i < ErrorKeys.Length; i++)
            {
                if (t.IndexOf(ErrorKeys[i], StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        public static void Apply(TextBlock box, string text)
        {
            Paint(box, text, IsError(text) ? 1 : (IsOk(text) ? 0 : 2));
        }

        public static void Apply(TextBlock box, string text, bool error)
        {
            if (error) Paint(box, text, 1);
            else if (IsOk(text)) Paint(box, text, 0);
            else Paint(box, text, 2);
        }

        /// <param name="kind">0 成功，1 错误，2 说明</param>
        private static void Paint(TextBlock box, string text, int kind)
        {
            if (box == null) return;
            box.Text = text ?? "";
            if (string.IsNullOrEmpty(box.Text))
            {
                SetBrush(box, "TextSecondaryBrush");
                return;
            }
            if (kind == 1)
            {
                SetBrush(box, "DangerBrush");
                return;
            }
            if (kind == 0)
            {
                if (!SetBrush(box, "OkBrush"))
                    SetBrush(box, "SuccessBrush");
                return;
            }
            SetBrush(box, "TextSecondaryBrush");
        }

        private static bool SetBrush(TextBlock box, string key)
        {
            try
            {
                var b = box.TryFindResource(key) as Brush;
                if (b == null) return false;
                box.Foreground = b;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
