using System.Collections.Generic;
using System.Text;

namespace IntraBox.Core
{
    /// <summary>把正则拆成片段，用极简短语解释每个符号（配合界面顶部的常用示例理解）。</summary>
    public static class RegexExplainer
    {
        public static string Explain(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return "";
            var sb = new StringBuilder();
            int i = 0;
            int n = pattern.Length;
            while (i < n)
            {
                char c = pattern[i];
                if (c == '\\' && i + 1 < n)
                {
                    sb.AppendLine(Esc(pattern[i + 1]));
                    i += 2;
                    continue;
                }
                if (c == '[')
                {
                    int end = pattern.IndexOf(']', i + 1);
                    if (end < 0) { sb.AppendLine("未闭合 ["); break; }
                    bool neg = i + 1 < n && pattern[i + 1] == '^';
                    string body = pattern.Substring(i + 1, end - i - 1);
                    if (neg) body = body.Substring(1);
                    sb.AppendLine(neg ? "非 " + body : body + " 之一");
                    i = end + 1;
                    continue;
                }
                if (c == '{')
                {
                    int end = pattern.IndexOf('}', i + 1);
                    if (end < 0) { sb.AppendLine("未闭合 {"); break; }
                    sb.AppendLine(Quant(pattern.Substring(i + 1, end - i - 1)));
                    i = end + 1;
                    continue;
                }
                if (c == '(')
                {
                    if (i + 2 < n && pattern[i + 1] == '?' && pattern[i + 2] == ':') { sb.AppendLine("非捕获分组"); i += 3; }
                    else if (i + 2 < n && pattern[i + 1] == '?' && pattern[i + 2] == '<') { sb.AppendLine("命名分组"); i += 3; }
                    else { sb.AppendLine("捕获分组"); i++; }
                    continue;
                }
                switch (c)
                {
                    case ')': sb.AppendLine("分组结束"); break;
                    case '^': sb.AppendLine("行首"); break;
                    case '$': sb.AppendLine("行尾"); break;
                    case '.': sb.AppendLine("任意字符"); break;
                    case '*': sb.AppendLine("0 次或多次"); break;
                    case '+': sb.AppendLine("1 次或多次"); break;
                    case '?': sb.AppendLine("0 次或 1 次"); break;
                    case '|': sb.AppendLine("或"); break;
                    default:
                    {
                        var lit = new StringBuilder();
                        while (i < n && IsLiteral(pattern[i])) { lit.Append(pattern[i]); i++; }
                        sb.AppendLine("字面 " + lit);
                        continue;
                    }
                }
                i++;
            }
            return sb.ToString().TrimEnd();
        }

        private static string Quant(string body)
        {
            var parts = body.Split(',');
            if (parts.Length == 1) return "恰好 " + parts[0] + " 次";
            if (parts.Length == 2)
            {
                if (string.IsNullOrEmpty(parts[1])) return "至少 " + parts[0] + " 次";
                return parts[0] + "~" + parts[1] + " 次";
            }
            return body;
        }

        private static string Esc(char n1)
        {
            switch (n1)
            {
                case 'd': return "数字 0~9";
                case 'D': return "非数字";
                case 'w': return "字母/数字/下划线";
                case 'W': return "非单词字符";
                case 's': return "空白字符";
                case 'S': return "非空白";
                case 'b': return "单词边界";
                case 'B': return "非单词边界";
                case 't': return "制表符";
                case 'n': return "换行";
                case 'r': return "回车";
                default: return "字面 " + n1;
            }
        }

        private static bool IsLiteral(char c)
        {
            switch (c)
            {
                case '\\': case '[': case '{': case '(': case ')':
                case '^': case '$': case '.': case '*': case '+': case '?': case '|':
                    return false;
                default:
                    return true;
            }
        }
    }
}
