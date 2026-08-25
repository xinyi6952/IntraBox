namespace IntraBox.Modules.Formatter
{
    /// <summary>
    /// 文本格式化引擎接口：每种格式实现「美化」与「压缩」。
    /// 约定：出错时返回 null 并通过 error 输出错误信息（携带具体位置/原因），
    /// 界面据此做非阻断式红字提示，不崩溃、保留原输入。
    /// </summary>
    public interface ITextFormatter
    {
        /// <summary>格式名称（显示在下拉框中）</summary>
        string FormatName { get; }

        /// <summary>
        /// 美化（缩进、换行）。indent 为缩进字符串："\t"（Tab）/ "  "（2 空格）/ "    "（4 空格）。
        /// 失败返回 null 并输出 error。
        /// </summary>
        string Beautify(string input, string indent, out string error);

        /// <summary>压缩（去空白、单行），失败返回 null 并输出 error</summary>
        string Minify(string input, out string error);
    }
}
