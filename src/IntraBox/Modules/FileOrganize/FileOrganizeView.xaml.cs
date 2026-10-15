using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;
using WinForms = System.Windows.Forms;

namespace IntraBox.Modules.FileOrganize
{
    /// <summary>桌面/目录文件整理：多规则（条件+目标目录变量）→ 预览树 → 确认移动 → 可撤销。</summary>
    public partial class FileOrganizeView : System.Windows.Controls.UserControl, IModuleView
    {
        private static readonly string[] KindNames = { "扩展名", "文件名包含", "正则", "大于指定大小", "早于指定日期" };

        private readonly ObservableCollection<RuleRow> _rules = new ObservableCollection<RuleRow>();
        private bool _loading;
        private bool _persisting;
        private bool _syncingDateBar;
        private CancellationTokenSource _workCts;

        public FileOrganizeView()
        {
            InitializeComponent();
            RuleGrid.ItemsSource = _rules;
            _rules.CollectionChanged += Rules_CollectionChanged;
        }

        public void OnActivated()
        {
            FileOrganizeStore.Notice -= OnStoreNotice;
            FileOrganizeStore.Notice += OnStoreNotice;
            _loading = true;
            _rules.Clear();

            var saved = FileOrganizeStore.Load(FileOrganizeStore.DefaultPath);
            if (saved != null)
            {
                ApplyState(saved);
            }
            else
            {
                // 仅迁移旧版写在 history.json 里的规则，之后只维护 fileorganize.json
                Dictionary<string, object> state;
                if (HistoryManager.TryLoad("fileorganize", out state))
                {
                    DirBox.Text = HistoryManager.GetString(state, "dir");
                    SubDirCheck.IsChecked = HistoryManager.GetBool(state, "sub", false);
                    LoadRulesList(ParseRulesJson(HistoryManager.GetString(state, "rules")));
                }
            }

            if (_rules.Count == 0)
            {
                _rules.Add(new RuleRow { KindName = "扩展名", Pattern = "jpg", TargetTemplate = "图片", Enabled = true });
                _rules.Add(new RuleRow { KindName = "扩展名", Pattern = "png", TargetTemplate = "图片", Enabled = true });
                _rules.Add(new RuleRow { KindName = "扩展名", Pattern = "", TargetTemplate = "{type}", Enabled = true });
            }
            if (string.IsNullOrWhiteSpace(DirBox.Text))
                DirBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            EnsureAtLeastOneEnabled();
            _loading = false;
            PersistNow();
            RefreshDateBar();
            RefreshUndoButton();
            FormHint.Apply(MsgText, "先预览并勾选要移动的文件。默认全部不勾选，只有勾选的才会整理；未勾选的保持原位。占用或无权限的文件会跳过。");
            string notice = FileOrganizeStore.TakeNotice();
            if (!string.IsNullOrEmpty(notice)) FormHint.Apply(MsgText, notice, true);
        }

        private void OnStoreNotice(string msg)
        {
            if (!string.IsNullOrEmpty(msg)) FormHint.Apply(MsgText, msg, true);
        }

        public void OnDeactivated()
        {
            FileOrganizeStore.Notice -= OnStoreNotice;
            CancelWork();
            CommitGrid();
            PersistNow();
        }

        private void CancelWork()
        {
            if (_workCts != null) _workCts.Cancel();
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
                {
                    DirBox.Text = dlg.SelectedPath;
                    PersistNow();
                }
            }
        }

        private void DirBox_LostFocus(object sender, RoutedEventArgs e)
        {
            PersistNow();
        }

        private void OptionChanged_Click(object sender, RoutedEventArgs e)
        {
            PersistNow();
        }

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            _rules.Add(new RuleRow { KindName = "扩展名", Pattern = "", TargetTemplate = "{type}", Enabled = true });
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var row = btn != null ? btn.DataContext as RuleRow : null;
            DeleteRule(row);
        }

        private void RemoveRule_Click(object sender, RoutedEventArgs e)
        {
            DeleteRule(RuleGrid.SelectedItem as RuleRow);
        }

        private void DeleteRule(RuleRow row)
        {
            if (row == null)
            {
                MessageBox.Show("请先选中要删除的规则，或点该行右侧的「删除」。", "删除规则",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_rules.Count <= 1)
            {
                MessageBox.Show("至少保留并勾选一条规则，不能删光。", "删除规则",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string pattern = string.IsNullOrWhiteSpace(row.Pattern) ? "（空）" : row.Pattern.Trim();
            string target = string.IsNullOrWhiteSpace(row.TargetTemplate) ? "（空）" : row.TargetTemplate.Trim();
            string detail = "匹配条件：" + row.KindName
                + "\n条件值：" + pattern
                + "\n目标目录：" + target
                + (row.Enabled ? "" : "\n（当前未启用）");
            if (!ConfirmHelper.Delete(detail)) return;
            RuleGrid.SelectedItem = row;
            _rules.Remove(row);
            EnsureAtLeastOneEnabled();
            RefreshDateBar();
        }

        private void EnableCheck_Click(object sender, RoutedEventArgs e)
        {
            var box = sender as CheckBox;
            var row = box != null ? box.DataContext as RuleRow : null;
            if (row == null) return;
            RuleGrid.SelectedItem = row;
            RuleGrid.CurrentItem = row;
            if (EnabledCount() == 0)
            {
                row.Enabled = true;
                if (box != null) box.IsChecked = true;
                FormHint.Apply(MsgText, "至少勾选一条规则。");
                return;
            }
            PersistNow();
        }

        private void KindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var combo = sender as ComboBox;
            var row = combo != null ? combo.DataContext as RuleRow : null;
            if (row != null)
                RuleGrid.SelectedItem = row;
            RefreshDateBar();
            PersistNow();
        }

        private void RuleField_LostFocus(object sender, RoutedEventArgs e)
        {
            PersistNow();
        }

        private void RuleDate_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _syncingDateBar) return;
            var row = RuleGrid.SelectedItem as RuleRow;
            if (row == null || RuleDatePicker == null) return;
            row.DateValue = RuleDatePicker.SelectedDate;
            PersistNow();
        }

        private void RuleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            RefreshDateBar();
        }

        private void RuleGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            PersistNow();
        }

        private void RefreshDateBar()
        {
            if (DateBar == null || RuleDatePicker == null) return;
            var row = RuleGrid.SelectedItem as RuleRow;
            bool show = row != null && row.KindName == "早于指定日期";
            DateBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _syncingDateBar = true;
            try
            {
                RuleDatePicker.SelectedDate = show ? row.DateValue : null;
            }
            finally
            {
                _syncingDateBar = false;
            }
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            CommitGrid();
            PersistNow();
            if (EnabledCount() == 0)
            {
                FormHint.Apply(MsgText, "至少勾选一条规则后再预览。");
                return;
            }
            string dir = (DirBox.Text ?? "").Trim();
            if (!Directory.Exists(dir))
            {
                FormHint.Apply(MsgText, "目录不存在。");
                return;
            }
            var rules = ToRules();
            bool sub = SubDirCheck.IsChecked == true;
            CancelWork();
            var cts = new CancellationTokenSource();
            _workCts = cts;
            LoadingOverlay.Show(this, "正在扫描…", CancelWork);
            List<OrganizePlanItem> plan = null;
            string scanErr = null;
            bool scanCancelled = false;
            try
            {
                plan = await Task.Run(() => OrganizeEngine.BuildPlan(dir, sub, rules, cts.Token), cts.Token);
            }
            catch (OperationCanceledException) { scanCancelled = true; }
            catch (Exception ex) { scanErr = ex.RootMessage(); }
            finally
            {
                LoadingOverlay.Hide(this);
                if (ReferenceEquals(_workCts, cts)) _workCts = null;
            }
            if (!IsLoaded) return;
            if (scanCancelled)
            {
                FormHint.Apply(MsgText, "已取消扫描，未改动文件。");
                return;
            }
            if (scanErr != null)
            {
                FormHint.Apply(MsgText, "扫描失败：" + scanErr, true);
                return;
            }
            if (plan == null || plan.Count == 0)
            {
                FormHint.Apply(MsgText, "没有匹配到可移动的文件。");
                return;
            }

            var preview = new OrganizePreviewWindow(plan);
            preview.Owner = Window.GetWindow(this);
            if (preview.ShowDialog() != true) return;

            var chosen = preview.Chosen;
            if (chosen == null || chosen.Count == 0)
            {
                FormHint.Apply(MsgText, "没有勾选任何文件，未改动磁盘。默认全部不勾选，只有选中的才会整理。");
                return;
            }

            await MoveChosenAsync(chosen);
        }

        private async Task MoveChosenAsync(IList<OrganizePlanItem> chosen)
        {
            CancelWork();
            var cts = new CancellationTokenSource();
            _workCts = cts;
            int total = chosen.Count;
            LoadingOverlay.Show(this, "正在移动 0/" + total, CancelWork);
            var moved = new List<OrganizePlanItem>();
            int ok = 0, skip = 0;
            var errors = new List<string>();
            bool cancelled = false;
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < chosen.Count; i++)
                    {
                        if (cts.Token.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }
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
                            }
                            else
                            {
                                File.Move(item.FromPath, item.ToPath);
                                moved.Add(item);
                                ok++;
                            }
                        }
                        catch (Exception ex)
                        {
                            skip++;
                            errors.Add(item.Name + "：" + FriendlyIo(ex));
                        }
                        int done = i + 1;
                        Dispatcher.BeginInvoke(new Action(() =>
                            LoadingOverlay.SetText(this, "正在移动 " + done + "/" + total)));
                    }
                });
            }
            catch (Exception ex)
            {
                errors.Add(ex.RootMessage());
            }
            finally
            {
                LoadingOverlay.Hide(this);
                if (ReferenceEquals(_workCts, cts)) _workCts = null;
            }
            if (!IsLoaded) return;
            if (moved.Count > 0)
                FileOrganizeStore.SetLastUndo(moved);
            PersistNow();
            FileOrganizeStore.Flush();
            RefreshUndoButton();
            string head = cancelled
                ? "已取消。已移动 " + ok + " 个，这些文件留在新位置。"
                : "完成：成功 " + ok + "，跳过 " + skip + "。";
            FormHint.Apply(MsgText, head
                + (ok > 0 ? " 可用「撤销上次整理」把刚移动的文件移回原处。" : "")
                + (skip > 0 && !cancelled ? "" : "")
                + (errors.Count == 0 ? "" : "\n" + string.Join("\n", errors)), errors.Count > 0);
        }

        private async void Undo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var moves = FileOrganizeStore.CopyLastUndo();
                if (moves.Count == 0)
                {
                    MessageBox.Show(
                        "没有可撤销的整理。\n\n请先点「预览并执行」，勾选要移动的文件，并且至少成功移动一个文件后，才能撤销。",
                        "撤销上次整理",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    RefreshUndoButton();
                    return;
                }

                string summary = FileOrganizeStore.FormatUndoSummary(moves, 12);
                if (!ConfirmHelper.Action("确定撤销上次整理？\n\n" + summary, "撤销确认"))
                    return;

                await UndoMovesAsync(moves);
            }
            catch (Exception ex)
            {
                MessageBox.Show("撤销失败：" + ex.RootMessage(), "撤销上次整理",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task UndoMovesAsync(List<OrganizePlanItem> moves)
        {
            CancelWork();
            var cts = new CancellationTokenSource();
            _workCts = cts;
            int total = moves.Count;
            LoadingOverlay.Show(this, "正在撤销 0/" + total, CancelWork);
            int ok = 0, skip = 0;
            bool cancelled = false;
            int stopAt = -1;
            var skipReasons = new List<string>();
            var failed = new List<OrganizePlanItem>();
            try
            {
                await Task.Run(() =>
                {
                    for (int i = moves.Count - 1; i >= 0; i--)
                    {
                        if (cts.Token.IsCancellationRequested)
                        {
                            cancelled = true;
                            stopAt = i;
                            break;
                        }
                        var item = moves[i];
                        try
                        {
                            if (item == null || string.IsNullOrEmpty(item.ToPath) || string.IsNullOrEmpty(item.FromPath))
                            {
                                skip++;
                            }
                            else if (!File.Exists(item.ToPath))
                            {
                                skip++;
                                skipReasons.Add((item.Name ?? item.ToPath) + "：整理后的文件已不在");
                            }
                            else if (File.Exists(item.FromPath))
                            {
                                skip++;
                                skipReasons.Add((item.Name ?? item.FromPath) + "：原位置已有同名文件");
                            }
                            else
                            {
                                var srcDir = Path.GetDirectoryName(item.FromPath);
                                if (!string.IsNullOrEmpty(srcDir) && !Directory.Exists(srcDir))
                                    Directory.CreateDirectory(srcDir);
                                File.Move(item.ToPath, item.FromPath);
                                ok++;
                            }
                        }
                        catch (Exception ex)
                        {
                            skip++;
                            skipReasons.Add((item != null ? item.Name : "") + "：" + FriendlyIo(ex));
                            if (item != null) failed.Add(item);
                        }
                        int done = total - i;
                        Dispatcher.BeginInvoke(new Action(() =>
                            LoadingOverlay.SetText(this, "正在撤销 " + done + "/" + total)));
                    }
                });
            }
            finally
            {
                LoadingOverlay.Hide(this);
                if (ReferenceEquals(_workCts, cts)) _workCts = null;
            }
            if (!IsLoaded) return;
            if (cancelled || failed.Count > 0)
            {
                var remain = new List<OrganizePlanItem>();
                if (cancelled && stopAt >= 0)
                {
                    for (int j = 0; j <= stopAt; j++)
                        if (moves[j] != null) remain.Add(moves[j]);
                }
                for (int j = 0; j < failed.Count; j++)
                    remain.Add(failed[j]);
                if (remain.Count > 0) FileOrganizeStore.SetLastUndo(remain);
                else FileOrganizeStore.ClearLastUndo();
            }
            else
                FileOrganizeStore.ClearLastUndo();
            PersistNow();
            FileOrganizeStore.Flush();
            RefreshUndoButton();
            string msg = cancelled
                ? "已取消。已撤销 " + ok + " 个。"
                : "已撤销 " + ok + " 个文件" + (skip > 0 ? "，跳过 " + skip : "") + "。";
            if (!cancelled && skipReasons.Count > 0)
                msg += "\n" + string.Join("\n", skipReasons.ToArray());
            FormHint.Apply(MsgText, msg, skip > 0 && ok == 0);
            MessageBox.Show(msg, "撤销上次整理", MessageBoxButton.OK,
                skip > 0 && ok == 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        private void RefreshUndoButton()
        {
            if (UndoBtn == null) return;
            bool has = FileOrganizeStore.CopyLastUndo().Count > 0;
            UndoBtn.IsEnabled = true;
            UndoBtn.ToolTip = has
                ? "把最近一次整理移走的文件移回原处"
                : "还没有可撤销的整理。请先预览并执行，成功移动文件后再撤销。";
        }

        private void CommitGrid()
        {
            try
            {
                RuleGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                RuleGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }
        }

        private void Rules_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_loading) PersistNow();
        }

        private int EnabledCount()
        {
            int n = 0;
            for (int i = 0; i < _rules.Count; i++)
            {
                if (_rules[i].Enabled) n++;
            }
            return n;
        }

        private void EnsureAtLeastOneEnabled()
        {
            if (_rules.Count == 0 || EnabledCount() > 0) return;
            _rules[0].Enabled = true;
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
                    TargetTemplate = string.IsNullOrWhiteSpace(r.TargetTemplate) ? "{type}" : r.TargetTemplate.Trim(),
                    Enabled = r.Enabled
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

        private void ApplyState(FileOrganizeState saved)
        {
            DirBox.Text = saved.Dir ?? "";
            SubDirCheck.IsChecked = saved.Sub;
            LoadRulesList(saved.Rules);
        }

        private void LoadRulesList(IList<OrganizeRule> list)
        {
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
                    TargetTemplate = r.TargetTemplate ?? "{type}",
                    Enabled = r.Enabled
                });
            }
        }

        private static List<OrganizeRule> ParseRulesJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<OrganizeRule>();
            try
            {
                var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<OrganizeRule>>(json);
                return list ?? new List<OrganizeRule>();
            }
            catch
            {
                return new List<OrganizeRule>();
            }
        }

        /// <summary>把当前规则写入内存并排队落盘到 fileorganize.json。</summary>
        private void PersistNow()
        {
            // CellEditEnding 与 CollectionChanged 可能叠加重入，避免同一次交互排队两次 Save。
            if (_loading || _persisting) return;
            _persisting = true;
            try
            {
                var state = new FileOrganizeState
                {
                    Dir = DirBox.Text ?? "",
                    Sub = SubDirCheck.IsChecked == true,
                    Rules = ToRules()
                };
                FileOrganizeStore.Save(FileOrganizeStore.DefaultPath, state);
            }
            finally
            {
                _persisting = false;
            }
        }

        private static string FriendlyIo(Exception ex)
        {
            if (ex is UnauthorizedAccessException) return "无权限";
            if (ex is IOException) return "文件被占用或无法移动";
            return ex.RootMessage();
        }

        private sealed class RuleRow : INotifyPropertyChanged
        {
            private string _kindName = "扩展名";
            private string _pattern = "";
            private string _target = "{type}";
            private bool _enabled = true;

            public string[] KindChoices { get { return KindNames; } }

            public bool Enabled
            {
                get { return _enabled; }
                set
                {
                    if (_enabled == value) return;
                    _enabled = value;
                    OnChanged("Enabled");
                }
            }

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
                    OnChanged("Pattern");
                    OnChanged("DateValue");
                }
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
                    string next = value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "";
                    if (next == _pattern) return;
                    _pattern = next;
                    OnChanged("Pattern");
                    OnChanged("DateValue");
                }
            }

            public string Pattern
            {
                get { return _pattern; }
                set
                {
                    string next = value ?? "";
                    if (next == _pattern) return;
                    _pattern = next;
                    OnChanged("Pattern");
                    OnChanged("DateValue");
                }
            }

            public string TargetTemplate
            {
                get { return _target; }
                set
                {
                    string next = value ?? "";
                    if (next == _target) return;
                    _target = next;
                    OnChanged("TargetTemplate");
                }
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
