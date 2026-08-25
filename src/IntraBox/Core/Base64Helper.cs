using System;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>
    /// Base64 编解码纯逻辑（供界面与测试共用）。
    /// </summary>
    public static class Base64Helper
    {
        /// <summary>文本 UTF-8 编码为 Base64。</summary>
        public static string Encode(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));
        }

        /// <summary>Base64 解码为 UTF-8 文本；非法时抛 FormatException。</summary>
        public static string Decode(string base64)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
    }
}
