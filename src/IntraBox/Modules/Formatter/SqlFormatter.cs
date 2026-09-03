using System;
using System.Collections.Generic;
using System.Text;
using IntraBox.Core;

namespace IntraBox.Modules.Formatter
{
    /// <summary>
    /// SQL 格式化：关键字统一大写 + 子句清晰换行 + 合理缩进。
    /// 规则：SELECT/FROM/WHERE/ORDER BY 等子句独占一行；字段、条件、JOIN 各自换行缩进；
    /// AND/OR 位于行首；操作符左右加空格。
    /// 说明：MVP 采用词法分析 + 启发式排版，对复杂嵌套/子查询做基础处理。
    /// </summary>
    public sealed class SqlFormatter : ITextFormatter
    {
        public string FormatName => "SQL";
        public override string ToString() { return FormatName; }

        public string Beautify(string input, string indent, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            try
            {
                return FormatSql(input, indent);
            }
            catch (Exception ex)
            {
                error = ex.RootMessage();
                return null;
            }
        }

        public string Minify(string input, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return input;
            try
            {
                return System.Text.RegularExpressions.Regex.Replace(input, @"\s+", " ").Trim();
            }
            catch (Exception ex)
            {
                error = ex.RootMessage();
                return null;
            }
        }

        // ---------- 词法分析 ----------

        private enum TokenType { Keyword, Identifier, Operator, Comma, Semicolon, LParen, RParen, Dot, Literal, Star }

        private sealed class Tok
        {
            public TokenType Type;
            public string Text;
        }

        private enum Clause { None, Select, From, Join, On, Where, GroupBy, OrderBy, Having, Limit, Offset }

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "GROUP", "ORDER", "BY", "HAVING", "LIMIT", "OFFSET",
            "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "FULL", "CROSS", "ON",
            "AND", "OR", "NOT", "NULL", "IS", "IN", "LIKE", "BETWEEN", "EXISTS",
            "AS", "DISTINCT", "ASC", "DESC", "UNION", "ALL",
            "INSERT", "INTO", "UPDATE", "SET", "DELETE", "VALUES",
            "CASE", "WHEN", "THEN", "ELSE", "END"
        };

        private static List<Tok> Tokenize(string sql)
        {
            var tokens = new List<Tok>();
            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];

                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (c == '\'') // 字符串字面量（含 '' 转义）
                {
                    int start = i;
                    i++;
                    while (i < sql.Length)
                    {
                        if (sql[i] == '\'')
                        {
                            if (i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }
                            i++;
                            break;
                        }
                        i++;
                    }
                    tokens.Add(new Tok { Type = TokenType.Literal, Text = sql.Substring(start, i - start) });
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                    var word = sql.Substring(start, i - start);
                    tokens.Add(new Tok { Type = Keywords.Contains(word) ? TokenType.Keyword : TokenType.Identifier, Text = word });
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < sql.Length && char.IsDigit(sql[i])) i++;
                    tokens.Add(new Tok { Type = TokenType.Literal, Text = sql.Substring(start, i - start) });
                    continue;
                }

                if (c == ',') { tokens.Add(new Tok { Type = TokenType.Comma, Text = "," }); i++; continue; }
                if (c == ';') { tokens.Add(new Tok { Type = TokenType.Semicolon, Text = ";" }); i++; continue; }
                if (c == '(') { tokens.Add(new Tok { Type = TokenType.LParen, Text = "(" }); i++; continue; }
                if (c == ')') { tokens.Add(new Tok { Type = TokenType.RParen, Text = ")" }); i++; continue; }
                if (c == '.') { tokens.Add(new Tok { Type = TokenType.Dot, Text = "." }); i++; continue; }
                if (c == '*') { tokens.Add(new Tok { Type = TokenType.Star, Text = "*" }); i++; continue; }

                if ("=><!+-/%".IndexOf(c) >= 0)
                {
                    int start = i;
                    if (i + 1 < sql.Length && (sql[i + 1] == '=' || (sql[i] == '<' && sql[i + 1] == '>')))
                        i += 2;
                    else
                        i++;
                    tokens.Add(new Tok { Type = TokenType.Operator, Text = sql.Substring(start, i - start) });
                    continue;
                }

                i++; // 未知字符跳过
            }
            return tokens;
        }

        // ---------- 格式化 ----------

        private static string FormatSql(string input, string indent)
        {
            var tokens = Tokenize(input);
            if (tokens.Count == 0) return input;

            var sb = new StringBuilder();
            var clause = Clause.None;

            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];

                // 1. 子句关键字（含组合：GROUP BY / ORDER BY / LEFT JOIN 等）
                string combined = TryCombine(tokens, ref i, out bool isJoin, out bool isInline);
                if (combined != null)
                {
                    if (isJoin)
                    {
                        // JOIN 与主表同级缩进
                        sb.Append('\n');
                        AppendIndent(sb, indent, 1);
                        sb.Append(combined);
                        sb.Append(' ');
                        clause = Clause.Join;
                    }
                    else
                    {
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(combined);
                        if (isInline)
                        {
                            // LIMIT / OFFSET：值与关键字同行
                            sb.Append(' ');
                        }
                        else
                        {
                            sb.Append('\n');
                            AppendIndent(sb, indent, 1);
                        }
                        clause = GetClause(combined);
                    }
                    continue;
                }

                // 2. ON 紧跟 JOIN
                if (t.Type == TokenType.Keyword && t.Text.Equals("ON", StringComparison.OrdinalIgnoreCase))
                {
                    AppendSpace(sb);
                    sb.Append("ON ");
                    clause = Clause.On;
                    continue;
                }

                // 3. AND / OR 行首
                if (t.Type == TokenType.Keyword &&
                    (t.Text.Equals("AND", StringComparison.OrdinalIgnoreCase) || t.Text.Equals("OR", StringComparison.OrdinalIgnoreCase)))
                {
                    sb.Append('\n');
                    AppendIndent(sb, indent, 1);
                    sb.Append(t.Text.ToUpperInvariant()).Append(' ');
                    continue;
                }

                // 4. 逗号：SELECT / ORDER BY / GROUP BY 中换行
                if (t.Type == TokenType.Comma)
                {
                    bool newline = clause == Clause.Select || clause == Clause.OrderBy || clause == Clause.GroupBy;
                    sb.Append(',');
                    if (newline) { sb.Append('\n'); AppendIndent(sb, indent, 1); }
                    else sb.Append(' ');
                    continue;
                }

                // 5. 其他 token
                switch (t.Type)
                {
                    case TokenType.Operator:
                        sb.Append(' ').Append(t.Text).Append(' ');
                        break;
                    case TokenType.Dot:
                        sb.Append('.');
                        break;
                    case TokenType.LParen:
                        // 函数调用紧贴，其它前加空格
                        if (sb.Length > 0 && (char.IsLetterOrDigit(sb[sb.Length - 1]) || sb[sb.Length - 1] == '_'))
                            sb.Append('(');
                        else { AppendSpace(sb); sb.Append('('); }
                        break;
                    case TokenType.RParen:
                        sb.Append(')');
                        break;
                    case TokenType.Semicolon:
                        sb.Append(';');
                        break;
                    case TokenType.Star:
                        AppendSpace(sb);
                        sb.Append('*');
                        break;
                    default:
                        AppendSpace(sb);
                        sb.Append(t.Type == TokenType.Keyword ? t.Text.ToUpperInvariant() : t.Text);
                        break;
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>识别子句关键字（含组合），返回大写的子句名；非子句返回 null。</summary>
        private static string TryCombine(List<Tok> tokens, ref int i, out bool isJoin, out bool isInline)
        {
            isJoin = false;
            isInline = false;
            if (tokens[i].Type != TokenType.Keyword) return null;
            string upper = tokens[i].Text.ToUpperInvariant();

            // JOIN 系列
            if (upper == "LEFT" || upper == "RIGHT" || upper == "INNER" || upper == "OUTER" || upper == "FULL" || upper == "CROSS")
            {
                if (Peek(tokens, i, "JOIN")) { i++; isJoin = true; return upper + " JOIN"; }
                return null;
            }
            if (upper == "JOIN") { isJoin = true; return "JOIN"; }

            // 组合关键字
            if (upper == "GROUP" && Peek(tokens, i, "BY")) { i++; return "GROUP BY"; }
            if (upper == "ORDER" && Peek(tokens, i, "BY")) { i++; return "ORDER BY"; }
            if (upper == "INSERT" && Peek(tokens, i, "INTO")) { i++; return "INSERT INTO"; }
            if (upper == "DELETE" && Peek(tokens, i, "FROM")) { i++; return "DELETE FROM"; }
            if (upper == "UNION" && Peek(tokens, i, "ALL")) { i++; return "UNION ALL"; }

            // 顶层子句
            if (upper == "SELECT" || upper == "FROM" || upper == "WHERE" || upper == "HAVING"
                || upper == "UNION" || upper == "UPDATE" || upper == "SET" || upper == "VALUES")
                return upper;

            // LIMIT / OFFSET 值同行
            if (upper == "LIMIT" || upper == "OFFSET") { isInline = true; return upper; }

            return null;
        }

        private static bool Peek(List<Tok> tokens, int i, string word)
        {
            return i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Keyword
                && tokens[i + 1].Text.Equals(word, StringComparison.OrdinalIgnoreCase);
        }

        private static Clause GetClause(string combined)
        {
            switch (combined)
            {
                case "SELECT": return Clause.Select;
                case "FROM": return Clause.From;
                case "WHERE": return Clause.Where;
                case "GROUP BY": return Clause.GroupBy;
                case "ORDER BY": return Clause.OrderBy;
                case "HAVING": return Clause.Having;
                case "LIMIT": return Clause.Limit;
                case "OFFSET": return Clause.Offset;
                default: return Clause.None;
            }
        }

        /// <summary>在需要时追加一个空格（行首/点后/左括号后不加）。</summary>
        private static void AppendSpace(StringBuilder sb)
        {
            if (sb.Length == 0) return;
            char c = sb[sb.Length - 1];
            if (c == ' ' || c == '\n' || c == '.' || c == '(') return;
            sb.Append(' ');
        }

        private static void AppendIndent(StringBuilder sb, string indent, int level)
        {
            for (int i = 0; i < level; i++) sb.Append(indent);
        }
    }
}
