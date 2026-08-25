using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.RegexTest
{
    /// <summary>
    /// 正则表达式测试：输入正则与测试文本，点击「测试」显示匹配结果列表。
    /// </summary>
    public partial class RegexTestView : UserControl, IModuleView
    {
        private readonly ObservableCollection<string> _matches = new ObservableCollection<string>();

        public RegexTestView()
        {
            InitializeComponent();
            MatchList.ItemsSource = _matches;
        }

        private void TestBtn_Click(object sender, RoutedEventArgs e)
        {
            Run();
        }

        private void PatternBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var p = PatternBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(p))
            {
                SetMsg("", false);
                return;
            }
            try
            {
                new Regex(p);
                SetMsg("", false);
            }
            catch (Exception ex)
            {
                SetMsg("正则错误：" + ex.Message, true);
            }
        }

        private void Run()
        {
            SetMsg("", false);
            _matches.Clear();

            var pattern = PatternBox.Text;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                SetMsg("请输入正则表达式", true);
                return;
            }
            if (string.IsNullOrWhiteSpace(TextEditor.Text))
            {
                SetMsg("请输入测试文本", true);
                return;
            }

            string sizeErr;
            if (!SizeLimits.TryCheckText(TextEditor.Text, out sizeErr))
            {
                SetMsg(sizeErr, true);
                return;
            }

            try
            {
                var options = RegexOptions.None;
                if (IgnoreCaseCheck.IsChecked == true) options |= RegexOptions.IgnoreCase;
                if (MultilineCheck.IsChecked == true) options |= RegexOptions.Multiline;

                var regex = new Regex(pattern, options, TimeSpan.FromSeconds(1));
                var ms = regex.Matches(TextEditor.Text);

                int n = 0;
                foreach (Match m in ms)
                {
                    _matches.Add("[" + m.Index + "] " + m.Value);
                    n++;
                    if (n >= 500)
                    {
                        _matches.Add("… 已截断，仅显示前 500 条匹配");
                        break;
                    }
                }

                if (n == 0) _matches.Add("（无匹配结果）");
                SetMsg(n == 0 ? "未匹配到任何内容" : "匹配到 " + n + " 条", n == 0);
            }
            catch (RegexMatchTimeoutException)
            {
                SetMsg("正则超时：模式可能存在灾难性回溯，请简化表达式。", true);
            }
            catch (ArgumentException ex)
            {
                SetMsg("正则错误：" + ex.Message, true);
            }
            catch (Exception ex)
            {
                SetMsg("匹配失败：" + ex.Message, true);
            }
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("regextest", out state)) return;

            IgnoreCaseCheck.IsChecked = HistoryManager.GetBool(state, "ignoreCase", false);
            MultilineCheck.IsChecked = HistoryManager.GetBool(state, "multiline", false);
            PatternBox.Text = HistoryManager.GetString(state, "pattern");
            TextEditor.Text = HistoryManager.GetString(state, "text");
            Run();
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("regextest", new Dictionary<string, object>
            {
                { "pattern", PatternBox.Text ?? "" },
                { "text", TextEditor.Text ?? "" },
                { "ignoreCase", IgnoreCaseCheck.IsChecked == true },
                { "multiline", MultilineCheck.IsChecked == true }
            });
        }
    }
}
