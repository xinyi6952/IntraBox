using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace IntraBox.Controls
{
    /// <summary>
    /// 基于大括号 {} 的折叠（AvalonEdit 官方 BraceFoldingStrategy 同思路）。
    /// 开闭括号在同一行时不折叠，避免干扰单行对象字面量。
    /// </summary>
    public sealed class BraceFoldingStrategy
    {
        public void UpdateFoldings(FoldingManager manager, TextDocument document)
        {
            if (manager == null || document == null) return;
            int firstErrorOffset;
            var foldings = CreateNewFoldings(document, out firstErrorOffset);
            manager.UpdateFoldings(foldings, firstErrorOffset);
        }

        public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset)
        {
            firstErrorOffset = -1;
            var newFoldings = new List<NewFolding>();
            var startOffsets = new Stack<int>();
            int lastNewLineOffset = 0;
            int len = document.TextLength;
            for (int i = 0; i < len; i++)
            {
                char c = document.GetCharAt(i);
                if (c == '{')
                {
                    startOffsets.Push(i);
                }
                else if (c == '}' && startOffsets.Count > 0)
                {
                    int startOffset = startOffsets.Pop();
                    if (startOffset < lastNewLineOffset)
                        newFoldings.Add(new NewFolding(startOffset, i + 1));
                }
                else if (c == '\n' || c == '\r')
                {
                    lastNewLineOffset = i + 1;
                }
            }
            newFoldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
            return newFoldings;
        }
    }
}
