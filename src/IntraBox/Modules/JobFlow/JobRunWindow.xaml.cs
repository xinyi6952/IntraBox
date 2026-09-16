using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IntraBox.Core;

namespace IntraBox.Modules.JobFlow
{
    public class JobRunWindow : Window
    {
        private readonly JobItem _job;
        private readonly Dictionary<string, FrameworkElement> _editors = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        private readonly Dictionary<string, CheckBox> _rememberChecks = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
        private CheckBox _rememberAll;
        private TextBlock _errBox;
        private string _secretWarn;

        public Dictionary<string, string> Inputs { get; private set; }

        public JobRunWindow(JobItem job)
        {
            _job = job;
            Title = "填写入参 — " + (job != null ? job.Name : "");
            Width = 440;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            BuildUi();
        }

        private void BuildUi()
        {
            var root = new DockPanel { Margin = new Thickness(12) };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var ok = new Button { Content = "运行", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            ok.Click += Ok_Click;
            var cancel = new Button { Content = "取消", Width = 88, IsCancel = true };
            cancel.Click += (s, e) => { DialogResult = false; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            Dictionary<string, string> remembered = null;
            if (_job != null && !string.IsNullOrEmpty(_job.Uid))
                remembered = JobSecretStore.Load(_job.Uid, out _secretWarn);

            var tipText = "密钥不进剪贴板历史。可勾选「记住密钥」用本机 DPAPI 保存（默认关）。必填项不能留空。";
            if (!string.IsNullOrEmpty(_secretWarn))
                tipText = _secretWarn + "。" + tipText;
            var tip = new TextBlock
            {
                Text = tipText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush
            };
            DockPanel.SetDock(tip, Dock.Top);
            root.Children.Add(tip);

            _rememberAll = new CheckBox
            {
                Content = "记住本次密钥入参（本机当前用户）",
                Margin = new Thickness(0, 0, 0, 8),
                IsChecked = false
            };
            _errBox = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(_errBox, Dock.Top);
            root.Children.Add(_errBox);

            DockPanel.SetDock(_rememberAll, Dock.Top);
            root.Children.Add(_rememberAll);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel();
            if (_job != null && _job.Params != null)
            {
                for (int i = 0; i < _job.Params.Count; i++)
                {
                    var p = _job.Params[i];
                    if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                    string name = p.Name.Trim();
                    string label = string.IsNullOrWhiteSpace(p.DisplayName) ? name : p.DisplayName.Trim();
                    var cap = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 8, 0, 4)
                    };
                    if (p.Required)
                    {
                        cap.Children.Add(new TextBlock
                        {
                            Text = "*",
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 0, 4, 0),
                            Foreground = TryFindResource("DangerBrush") as Brush ?? Brushes.IndianRed
                        });
                    }
                    cap.Children.Add(new TextBlock { Text = label });
                    panel.Children.Add(cap);
                    FrameworkElement editor;
                    if (p.Type == JobParamType.Secret)
                    {
                        var box = new PasswordBox();
                        string rememberedVal = null;
                        if (remembered != null)
                            remembered.TryGetValue(name, out rememberedVal);
                        if (!string.IsNullOrEmpty(rememberedVal))
                            box.Password = rememberedVal;
                        editor = box;
                        var rem = new CheckBox
                        {
                            Content = "记住密钥",
                            Margin = new Thickness(0, 4, 0, 0),
                            IsChecked = !string.IsNullOrEmpty(rememberedVal)
                        };
                        panel.Children.Add(box);
                        panel.Children.Add(rem);
                        _editors[name] = editor;
                        _rememberChecks[name] = rem;
                        continue;
                    }
                    if (p.Type == JobParamType.Bool)
                    {
                        var cb = new CheckBox
                        {
                            Content = "是",
                            IsChecked = string.Equals(p.DefaultValue, "1", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(p.DefaultValue, "true", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(p.DefaultValue, "是", StringComparison.Ordinal)
                        };
                        editor = cb;
                    }
                    else
                    {
                        var box = new TextBox { Text = p.DefaultValue ?? "" };
                        editor = box;
                    }
                    panel.Children.Add(editor);
                    _editors[name] = editor;
                }
            }
            scroll.Content = panel;
            root.Children.Add(scroll);
            Content = root;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            FormHint.Apply(_errBox, "");
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var toRemember = new Dictionary<string, string>(StringComparer.Ordinal);
            bool rememberAll = _rememberAll != null && _rememberAll.IsChecked == true;
            if (_job != null && _job.Params != null)
            {
                for (int i = 0; i < _job.Params.Count; i++)
                {
                    var p = _job.Params[i];
                    if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                    string name = p.Name.Trim();
                    FrameworkElement ed;
                    if (!_editors.TryGetValue(name, out ed)) continue;
                    string val = "";
                    var pwd = ed as PasswordBox;
                    var tb = ed as TextBox;
                    var cb = ed as CheckBox;
                    if (pwd != null) val = pwd.Password ?? "";
                    else if (tb != null) val = tb.Text ?? "";
                    else if (cb != null) val = cb.IsChecked == true ? "true" : "false";
                    if (p.Required && string.IsNullOrEmpty(val))
                    {
                        FormHint.Apply(_errBox, "请填写必填入参：" + name);
                        var ctl = ed as Control;
                        if (ctl != null) ctl.Focus();
                        return;
                    }
                    map[name] = val;
                    if (p.Type == JobParamType.Secret)
                    {
                        CheckBox rem;
                        bool remember = rememberAll;
                        if (_rememberChecks.TryGetValue(name, out rem) && rem.IsChecked == true)
                            remember = true;
                        if (remember)
                            toRemember[name] = val ?? "";
                        else if (rem != null && rem.IsChecked != true && !rememberAll)
                            toRemember[name] = ""; // 取消记住则清除该项
                    }
                }
            }
            if (toRemember.Count > 0 && _job != null && !string.IsNullOrEmpty(_job.Uid))
            {
                string err;
                if (!JobSecretStore.Save(_job.Uid, toRemember, out err))
                {
                    MessageBox.Show(err ?? "保存密钥失败", "记住密钥", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            Inputs = map;
            DialogResult = true;
        }
    }
}
