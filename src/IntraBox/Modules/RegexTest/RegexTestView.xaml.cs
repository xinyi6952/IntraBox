using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.RegexTest
{
    /// <summary>
    /// 正则表达式测试：输入正则与测试文本，点击「测试」显示匹配结果列表。
    /// </summary>
    public partial class RegexTestView : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }

        private readonly ObservableCollection<string> _matches = new ObservableCollection<string>();
        // 正则语法校验防抖：每敲一字都 new Regex 太频繁，停 300ms 再校验
        private readonly DispatcherTimer _validateDebounce = new DispatcherTimer();

        public RegexTestView()
        {
            InitializeComponent();
            MatchList.ItemsSource = _matches;
            _validateDebounce.Interval = TimeSpan.FromMilliseconds(300);
            _validateDebounce.Tick += (s, e) => { _validateDebounce.Stop(); ValidatePattern(); };
        }

        private void TestBtn_Click(object sender, RoutedEventArgs e)
        {
            Run();
        }

        private void PatternBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _validateDebounce.Stop();
            _validateDebounce.Start();
        }

        private void ValidatePattern()
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
                SetMsg("正则错误：" + ex.RootMessage(), true);
            }
        }

        private async void Run()
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

            var options = RegexOptions.None;
            if (IgnoreCaseCheck.IsChecked == true) options |= RegexOptions.IgnoreCase;
            if (MultilineCheck.IsChecked == true) options |= RegexOptions.Multiline;
            string text = TextEditor.Text;

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "匹配中…");

            List<string> result;
            int count;
            try
            {
                var r = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var regex = new Regex(pattern, options, TimeSpan.FromSeconds(1));
                    var ms = regex.Matches(text);
                    var list = new List<string>();
                    int n = 0;
                    foreach (Match m in ms)
                    {
                        token.ThrowIfCancellationRequested();
                        list.Add("[" + m.Index + "] " + m.Value);
                        n++;
                        if (n >= 500) { list.Add("… 已截断，仅显示前 500 条匹配"); break; }
                    }
                    return new { List = list, N = n };
                }, token);
                result = r.List;
                count = r.N;
            }
            catch (OperationCanceledException) { return; }
            catch (RegexMatchTimeoutException)
            {
                if (version == _gate.Version) SetMsg("正则超时：模式可能存在灾难性回溯，请简化表达式。", true);
                return;
            }
            catch (ArgumentException ex) { if (version == _gate.Version) SetMsg("正则错误：" + ex.RootMessage(), true); return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("匹配失败：" + ex.RootMessage(), true); return; }

            if (version != _gate.Version) return;

            foreach (var s in result) _matches.Add(s);
            if (count == 0) _matches.Add("（无匹配结果）");
            SetMsg(count == 0 ? "未匹配到任何内容" : "匹配到 " + count + " 条", count == 0);
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
            LoadingOverlay.Hide(this);
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
            _validateDebounce.Stop();
            CancelPending();
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
