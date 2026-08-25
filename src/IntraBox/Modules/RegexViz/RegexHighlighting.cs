using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace IntraBox.Modules.RegexViz
{
    /// <summary>把正则语法高亮注册进 AvalonEdit（元字符 / 量词 / 分组 / 转义 分色）。</summary>
    public static class RegexHighlighting
    {
        public const string Name = "RegEx";
        private static bool _ready;

        public static void Ensure()
        {
            if (_ready) return;
            _ready = true;
            try
            {
                using (var sr = new StringReader(Xshd))
                using (var reader = XmlReader.Create(sr))
                {
                    var xshd = HighlightingLoader.LoadXshd(reader);
                    var def = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
                    HighlightingManager.Instance.RegisterHighlighting(Name, new[] { ".regex" }, def);
                }
            }
            catch
            {
                _ready = false;
            }
        }

        private const string Xshd = @"<?xml version=""1.0""?>
<SyntaxDefinition name=""RegEx"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
  <Color name=""Escape"" foreground=""#D7BA7D"" />
  <Color name=""Meta"" foreground=""#C586C0"" />
  <Color name=""Quant"" foreground=""#4FC1FF"" />
  <Color name=""Group"" foreground=""#9CDCFE"" />
  <Color name=""Class"" foreground=""#CE9178"" />
  <Color name=""Anchor"" foreground=""#D16969"" />
  <RuleSet>
    <Span color=""Escape"">
      <Begin>\\</Begin>
      <RuleSet>
        <Rule>.</Rule>
      </RuleSet>
    </Span>
    <Span color=""Class"" multiline=""false"">
      <Begin>\[</Begin>
      <End>\]</End>
    </Span>
    <Span color=""Quant"">
      <Begin>\{</Begin>
      <End>\}</End>
    </Span>
    <Rule color=""Quant"">[*+?]</Rule>
    <Rule color=""Anchor"">[\^$]</Rule>
    <Rule color=""Meta"">[|.]</Rule>
    <Rule color=""Group"">[()]</Rule>
  </RuleSet>
</SyntaxDefinition>";
    }
}
