using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using IntraBox.Core;
using Newtonsoft.Json;
using WinForms = System.Windows.Forms;

namespace IntraBox.Modules.FileOrganize
{
    /// <summary>桌面/目录文件整理：多规则（条件+目标目录变量）→ 预览树 → 确认移动 → 可撤销。</summary>
    public partial class FileOrganizeView : System.Windows.Controls.UserControl, IModuleView
    {
        private static readonly string[] KindNames = { "扩展名", "文件名包含", "正则", "大于指定大小", "早于指定日期" };

        private readonly ObservableCollection<RuleRow> _rules = new ObservableCollection<RuleRow>();
        private readonly List<OrganizePlanItem> _lastMoves = new List<OrganizePlanItem>();

        public FileOrganizeView()
        {
            InitializeComponent();
            RuleGrid.ItemsSource = _rules;
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            _rules.Clear();
            if (HistoryManager.TryLoad("fileorganize", out state))
            {
                DirBox.Text = HistoryManager.GetString(state, "dir");
                SubDirCheck.IsChecked = HistoryManager.GetBool(state, "sub", false);
                LoadRulesJson(HistoryManager.GetString(state, "rules"));
            }
            if (_rules.Count == 0)
            {
                _rules.Add(new RuleRow { KindName = "扩展名", Pattern = "jpg", TargetTemplate = "图片" });
                _rules.Add(new RuleRow { KindName = "扩展名", Pattern = "png", TargetTemplate = "图片" });
                _rules.Add(new RuleRow { KindName = "扩展名", Pattern = "", TargetTemplate = "{type}" });
            }
            if (string.IsNullOrWhiteSpace(DirBox.Text))
                DirBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            MsgText.Text = "先预览并勾选要移动的文件。默认全部不勾选，只有勾选的才会整理；未勾选的保持原位。占用或无权限的文件会跳过。";
        }

        public void OnDeactivated()
        {
            CommitGrid();
            HistoryManager.Save("fileorganize", new Dictionary<string, object>
            {
                { "dir", DirBox.Text ?? "" },
                { "sub", SubDirCheck.IsChecked == true },
                { "rules", SaveRulesJson() }
            });
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择要整理的目录";
                dlg.ShowNewFolderButton = false;
                if (!string.IsNullOrWhiteSpace(DirBox.Text) && Directory.Exists(DirBox.Text))
                    dlg.SelectedPath = DirBox.Text;
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                    DirBox.Text = dlg.SelectedPath;
            }
        }

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            _rules.Add(new RuleRow { KindName = "扩展名", Pattern = "", TargetTemplate = "{type}" });
        }

        private void RemoveRule_Click(object sender, RoutedEventArgs e)
        {
            var row = RuleGrid.SelectedItem as RuleRow;
            if (row == null)
            {
                MsgText.Text = "请先选中要删除的规则。";
                return;
            }
            _rules.Remove(row);
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            CommitGrid();
            string dir = (DirBox.Text ?? "").Trim();
            if (!Directory.Exists(dir))
            {
                MsgText.Text = "目录不存在。";
                return;
            }
            var rules = ToRules();
            var plan = OrganizeEngine.BuildPlan(dir, SubDirCheck.IsChecked == true, rules);
            if (plan.Count == 0)
            {
                MsgText.Text = "没有匹配到可移动的文件。";
                return;
            }

            var preview = new OrganizePreviewWindow(plan);
            preview.Owner = Window.GetWindow(this);
            if (preview.ShowDialog() != true) return;

            var chosen = preview.Chosen;
            if (chosen == null || chosen.Count == 0)
            {
                MsgText.Text = "没有勾选任何文件，未改动磁盘。默认全部不勾选，只有选中的才会整理。";
                return;
            }

            _lastMoves.Clear();
            int ok = 0, skip = 0;
            var errors = new List<string>();
            for (int i = 0; i < chosen.Count; i++)
            {
                var item = chosen[i];
                try
                {
                    var destDir = Path.GetDirectoryName(item.ToPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    if (File.Exists(item.ToPath))
                    {
                        skip++;
                        errors.Add(item.Name + "：目标已存在");
                        continue;
                    }
                    File.Move(item.FromPath, item.ToPath);
                    _lastMoves.Add(item);
                    ok++;
                }
                catch (Exception ex)
                {
                    skip++;
                    errors.Add(item.Name + "：" + FriendlyIo(ex));
                }
            }
            MsgText.Text = "完成：成功 " + ok + "，跳过 " + skip + "。"
                + (errors.Count == 0 ? "" : "\n" + string.Join("\n", errors));
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_lastMoves.Count == 0)
            {
                MsgText.Text = "没有可撤销的整理。";
                return;
            }
            int ok = 0, skip = 0;
            for (int i = _lastMoves.Count - 1; i >= 0; i--)
            {
                var item = _lastMoves[i];
                try
                {
                    if (!File.Exists(item.ToPath)) { skip++; continue; }
                    if (File.Exists(item.FromPath)) { skip++; continue; }
                    var srcDir = Path.GetDirectoryName(item.FromPath);
                    if (!string.IsNullOrEmpty(srcDir) && !Directory.Exists(srcDir))
                        Directory.CreateDirectory(srcDir);
                    File.Move(item.ToPath, item.FromPath);
                    ok++;
                }
                catch { skip++; }
            }
            _lastMoves.Clear();
            MsgText.Text = "已撤销 " + ok + " 个文件" + (skip > 0 ? "，跳过 " + skip : "") + "。";
        }

        private void CommitGrid()
        {
            try
            {
                RuleGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
                RuleGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
            }
            catch { }
        }

        private List<OrganizeRule> ToRules()
        {
            var list = new List<OrganizeRule>();
            for (int i = 0; i < _rules.Count; i++)
            {
                var r = _rules[i];
                list.Add(new OrganizeRule
                {
                    Kind = KindIndex(r.KindName),
                    Pattern = r.Pattern ?? "",
                    TargetTemplate = string.IsNullOrWhiteSpace(r.TargetTemplate) ? "{type}" : r.TargetTemplate.Trim()
                });
            }
            return list;
        }

        private static int KindIndex(string name)
        {
            for (int i = 0; i < KindNames.Length; i++)
            {
                if (KindNames[i] == name) return i;
            }
            return 0;
        }

        private string SaveRulesJson()
        {
            try { return JsonConvert.SerializeObject(ToRules()); }
            catch { return "[]"; }
        }

        private void LoadRulesJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                var list = JsonConvert.DeserializeObject<List<OrganizeRule>>(json);
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    if (r == null) continue;
                    int k = r.Kind;
                    if (k < 0 || k >= KindNames.Length) k = 0;
                    _rules.Add(new RuleRow
                    {
                        KindName = KindNames[k],
                        Pattern = r.Pattern ?? "",
                        TargetTemplate = r.TargetTemplate ?? "{type}"
                    });
                }
            }
            catch { }
        }

        private static string FriendlyIo(Exception ex)
        {
            if (ex is UnauthorizedAccessException) return "无权限";
            if (ex is IOException) return "文件被占用或无法移动";
            return ex.Message;
        }

        private sealed class RuleRow : INotifyPropertyChanged
        {
            private string _kindName = "扩展名";
            private string _pattern = "";
            private string _target = "{type}";

            public string[] KindChoices { get { return KindNames; } }

            public string KindName
            {
                get { return _kindName; }
                set
                {
                    string next = value ?? "扩展名";
                    if (next == _kindName) return;
                    _kindName = next;
                    _pattern = "";
                    OnChanged("KindName");
                    OnChanged("IsDateKind");
                    OnChanged("Pattern");
                    OnChanged("DateValue");
                }
            }
            public bool IsDateKind
            {
                get { return _kindName == "早于指定日期"; }
            }
            public DateTime? DateValue
            {
                get
                {
                    DateTime d;
                    if (DateTime.TryParse(_pattern, out d)) return d.Date;
                    return null;
                }
                set
                {
                    _pattern = value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "";
                    OnChanged("Pattern");
                    OnChanged("DateValue");
                }
            }
            public string Pattern
            {
                get { return _pattern; }
                set
                {
                    _pattern = value ?? "";
                    OnChanged("Pattern");
                    OnChanged("DateValue");
                }
            }
            public string TargetTemplate
            {
                get { return _target; }
                set { _target = value; OnChanged("TargetTemplate"); }
            }
            public event PropertyChangedEventHandler PropertyChanged;
            private void OnChanged(string n)
            {
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs(n));
            }
        }
    }
}
