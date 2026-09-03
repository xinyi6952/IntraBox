using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.RegexViz
{
    public partial class RegexVizView : UserControl, IModuleView
    {
        private static readonly string[] SampleTexts =
        {
            "13800138000",
            "abc@example.com",
            "2024-01-15",
            "192.168.1.1"
        };

        public RegexVizView()
        {
            InitializeComponent();
            ExplainBox.MaximizeToggle += (s, e) =>
            {
                bool on = !ExplainBox.IsMaximized;
                InPlaceMaximize.Apply(on, ExplainBox, 3, 5, ToolbarPanel, InputCaption, PatternBox, SampleWrap);
                ExplainBox.IsMaximized = on;
            };
            PatternBox.TextChangedByUser += (s, e) => ValidatePattern();
        }

        public void OnActivated()
        {
            RegexHighlighting.Ensure();
            PatternBox.SetHighlightingByName(RegexHighlighting.Name);
            Dictionary<string, object> st;
            if (HistoryManager.TryLoad("regexviz", out st))
                PatternBox.Text = HistoryManager.GetString(st, "p");
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("regexviz", new Dictionary<string, object> { { "p", PatternBox.Text ?? "" } });
        }

        private void ValidatePattern()
        {
            var p = PatternBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(p))
            {
                MsgText.Text = "";
                return;
            }
            try
            {
                new Regex(p);
                MsgText.Text = "";
            }
            catch (Exception ex)
            {
                MsgText.Foreground = FindResource("DangerBrush") as System.Windows.Media.Brush;
                MsgText.Text = "正则错误：" + ex.RootMessage();
            }
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            var p = PatternBox.Text ?? "";
            try
            {
                var regex = new Regex(p);
                ExplainBox.Text = RegexExplainer.Explain(p);
                RegexHighlighting.Ensure();
                PatternBox.SetHighlightingByName(RegexHighlighting.Name);
                MsgText.Foreground = FindResource("OkBrush") as System.Windows.Media.Brush;
                MsgText.Text = "已解释";

                var sb = new StringBuilder();
                foreach (var s in SampleTexts)
                {
                    sb.AppendLine((regex.IsMatch(s) ? "✓ " : "✗ ") + s);
                }
                SampleBox.Text = sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                ExplainBox.Text = "";
                SampleBox.Text = "";
                MsgText.Foreground = FindResource("DangerBrush") as System.Windows.Media.Brush;
                MsgText.Text = "非法正则：" + ex.RootMessage();
            }
        }
    }
}
