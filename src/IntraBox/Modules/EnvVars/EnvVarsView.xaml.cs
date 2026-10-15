using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;
using WinForms = System.Windows.Forms;

namespace IntraBox.Modules.EnvVars
{
    /// <summary>
    /// 环境变量：浏览进程/用户/系统，只改用户级。
    /// </summary>
    public partial class EnvVarsView : UserControl, IModuleView, ILeaveGuard
    {
        private readonly List<EnvVarHelper.Item> _items = new List<EnvVarHelper.Item>();
        private readonly List<string> _pathParts = new List<string>();
        private List<EnvVarHelper.Item> _userItems = new List<EnvVarHelper.Item>();
        private List<EnvVarHelper.Item> _machineItems = new List<EnvVarHelper.Item>();
        private List<EnvVarHelper.Item> _processItems = new List<EnvVarHelper.Item>();
        private EnvVarHelper.Scope _scope = EnvVarHelper.Scope.User;
        private string _loadedName = "";
        private string _loadedValue = "";
        private bool _isNew;
        private bool _uiReady;
        private bool _suppress;
        private int _reloadGen;

        public EnvVarsView()
        {
            InitializeComponent();
            VarGrid.ItemsSource = _items;
            PathList.ItemsSource = _pathParts;
            FilterBar.Attach(VarGrid);
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (HistoryManager.TryLoad("envvars", out state))
            {
                int scope = HistoryManager.GetInt(state, "scope", 0);
                if (scope < 0 || scope > 2) scope = 0;
                SetScope((EnvVarHelper.Scope)scope, false);
            }
            Reload(true);
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("envvars", new Dictionary<string, object>
            {
                { "scope", (int)_scope }
            });
        }

        public bool CanLeave()
        {
            if (!IsDirty()) return true;
            var r = ConfirmHelper.Unsaved("用户环境变量有未保存的修改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return SaveCurrent();
            return true;
        }

        public bool HasUnsavedChanges()
        {
            return IsDirty();
        }

        private bool IsDirty()
        {
            if (!EnvVarHelper.CanEdit(_scope)) return false;
            string name = (NameBox.Text ?? "").Trim();
            string value = ValueBox.Text ?? "";
            if (_isNew) return name.Length > 0 || value.Length > 0;
            return !string.Equals(name, _loadedName, StringComparison.Ordinal)
                || !string.Equals(value, _loadedValue, StringComparison.Ordinal);
        }

        private EnvVarHelper.Scope CurrentScope()
        {
            if (ScopeMachine.IsChecked == true) return EnvVarHelper.Scope.Machine;
            if (ScopeProcess.IsChecked == true) return EnvVarHelper.Scope.Process;
            return EnvVarHelper.Scope.User;
        }

        private void SetScope(EnvVarHelper.Scope scope, bool reload)
        {
            _suppress = true;
            ScopeUser.IsChecked = scope == EnvVarHelper.Scope.User;
            ScopeMachine.IsChecked = scope == EnvVarHelper.Scope.Machine;
            ScopeProcess.IsChecked = scope == EnvVarHelper.Scope.Process;
            _suppress = false;
            _scope = scope;
            if (reload) Reload(true);
        }

        private void Scope_Checked(object sender, RoutedEventArgs e)
        {
            if (!_uiReady || _suppress) return;
            var next = CurrentScope();
            if (next == _scope) return;
            if (!ConfirmDiscardOrSave())
            {
                _suppress = true;
                SetScope(_scope, false);
                _suppress = false;
                return;
            }
            _scope = next;
            Reload(true);
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardOrSave()) return;
            Reload(true);
        }

        private void Reload(bool keepSelection)
        {
            Reload(keepSelection ? (NameBox.Text ?? "").Trim() : null);
        }

        private async void Reload(string keepName)
        {
            int gen = ++_reloadGen;
            if (ReloadBtn != null) ReloadBtn.IsEnabled = false;
            FormHint.Apply(MsgText, "正在读取…");
            List<EnvVarHelper.Item> user = null;
            List<EnvVarHelper.Item> machine = null;
            List<EnvVarHelper.Item> process = null;
            string err = null;
            try
            {
                await Task.Run(() =>
                {
                    user = EnvVarHelper.List(EnvVarHelper.Scope.User);
                    machine = EnvVarHelper.List(EnvVarHelper.Scope.Machine);
                    process = EnvVarHelper.List(EnvVarHelper.Scope.Process);
                });
            }
            catch (Exception ex)
            {
                err = ex.RootMessage();
            }
            if (gen != _reloadGen) return;
            if (ReloadBtn != null) ReloadBtn.IsEnabled = true;
            _uiReady = true;
            if (err != null)
            {
                FormHint.Apply(MsgText, "读取失败：" + err, true);
                return;
            }
            _userItems = user ?? new List<EnvVarHelper.Item>();
            _machineItems = machine ?? new List<EnvVarHelper.Item>();
            _processItems = process ?? new List<EnvVarHelper.Item>();

            List<EnvVarHelper.Item> src = _userItems;
            if (_scope == EnvVarHelper.Scope.Machine) src = _machineItems;
            else if (_scope == EnvVarHelper.Scope.Process) src = _processItems;

            _items.Clear();
            _items.AddRange(src);
            _suppress = true;
            VarGrid.Items.Refresh();
            FilterBar.Apply();
            if (!SelectByName(keepName) && _items.Count > 0)
                VarGrid.SelectedIndex = 0;
            else if (_items.Count == 0)
                VarGrid.SelectedItem = null;
            _suppress = false;
            ApplyEditEnabled();
            string countMsg = EnvVarHelper.ScopeText(_scope) + "变量 " + _items.Count + " 个"
                + (_scope == EnvVarHelper.Scope.User ? "，可新建/保存/删除。" : "，只读。");
            FormHint.Apply(MsgText, countMsg, false);

            var selected = VarGrid.SelectedItem as EnvVarHelper.Item;
            if (selected != null)
                LoadEditor(selected.Name, selected.Value, false);
            else
                LoadEditor("", "", false);
        }

        private bool SelectByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    VarGrid.SelectedIndex = i;
                    VarGrid.ScrollIntoView(_items[i]);
                    return true;
                }
            }
            return false;
        }

        private void ApplyEditEnabled()
        {
            bool edit = EnvVarHelper.CanEdit(_scope);
            NewBtn.IsEnabled = edit;
            SaveBtn.IsEnabled = edit;
            DeleteBtn.IsEnabled = edit;
            NameBox.IsReadOnly = !edit || !_isNew;
            ValueBox.IsReadOnly = !edit;
            PathUpBtn.IsEnabled = edit;
            PathDownBtn.IsEnabled = edit;
            PathAddBtn.IsEnabled = edit;
            PathDelBtn.IsEnabled = edit;
        }

        private void VarGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress) return;
            var item = VarGrid.SelectedItem as EnvVarHelper.Item;
            if (item == null) return;
            if (!_isNew && string.Equals(item.Name, _loadedName, StringComparison.OrdinalIgnoreCase) && !IsDirty())
                return;
            if (IsDirty())
            {
                var r = ConfirmHelper.Unsaved("用户环境变量有未保存的修改，是否保存？", "未保存确认");
                if (r == MessageBoxResult.Cancel)
                {
                    RestoreSelection();
                    return;
                }
                if (r == MessageBoxResult.Yes)
                {
                    // 写入当前编辑项后，选中保持用户点的目标行，不要 Reload 回 NameBox 里的旧名
                    if (!SaveCurrent(item.Name))
                    {
                        RestoreSelection();
                        return;
                    }
                    return;
                }
            }
            LoadEditor(item.Name, item.Value, false);
        }

        private void RestoreSelection()
        {
            _suppress = true;
            if (_isNew) VarGrid.SelectedItem = null;
            else SelectByName(_loadedName);
            _suppress = false;
        }

        private void LoadEditor(string name, string value, bool isNew)
        {
            _isNew = isNew;
            _loadedName = name ?? "";
            _loadedValue = value ?? "";
            _suppress = true;
            NameBox.Text = _loadedName;
            ValueBox.Text = _loadedValue;
            _suppress = false;
            ApplyEditEnabled();
            RefreshPathPanel();
            UpdateHint();
        }

        private void UpdateHint()
        {
            string name = (NameBox.Text ?? "").Trim();
            HintText.Text = EnvVarHelper.OtherScopeHint(
                name,
                EnvVarHelper.FindInScope(_userItems, name),
                EnvVarHelper.FindInScope(_machineItems, name),
                EnvVarHelper.FindInScope(_processItems, name));
        }

        private void RefreshPathPanel()
        {
            string name = (NameBox.Text ?? "").Trim();
            bool path = EnvVarHelper.IsPathName(name);
            PathPanel.Visibility = path ? Visibility.Visible : Visibility.Collapsed;
            if (!path) return;
            _pathParts.Clear();
            var parts = EnvVarHelper.SplitPath(ValueBox.Text);
            for (int i = 0; i < parts.Length; i++)
                _pathParts.Add(parts[i]);
            PathList.Items.Refresh();
        }

        private void SyncPathToValue()
        {
            ValueBox.Text = EnvVarHelper.JoinPath(_pathParts);
            PathList.Items.Refresh();
        }

        private void ValueBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (EnvVarHelper.IsPathName((NameBox.Text ?? "").Trim()))
                RefreshPathPanel();
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            if (!EnvVarHelper.CanEdit(_scope)) return;
            if (!ConfirmDiscardOrSave()) return;
            _suppress = true;
            VarGrid.SelectedItem = null;
            _suppress = false;
            LoadEditor("", "", true);
            NameBox.IsReadOnly = false;
            NameBox.Focus();
            FormHint.Apply(MsgText, "填写名称和值后保存。名称为空不能保存。", false);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrent();
        }

        private bool SaveCurrent()
        {
            return SaveCurrent((NameBox.Text ?? "").Trim());
        }

        private bool SaveCurrent(string keepName)
        {
            if (!EnvVarHelper.CanEdit(_scope))
            {
                FormHint.Apply(MsgText, "系统与进程变量只读，只能改用户级。", true);
                return false;
            }
            string name = (NameBox.Text ?? "").Trim();
            string value = ValueBox.Text ?? "";
            string err;
            if (!EnvVarHelper.TryValidateName(name, out err))
            {
                FormHint.Apply(MsgText, err);
                return false;
            }
            bool deleting = string.IsNullOrEmpty(value);
            if (deleting)
            {
                if (_isNew)
                {
                    FormHint.Apply(MsgText, "未填写值，无法新建。");
                    return false;
                }
                if (!ConfirmHelper.Action("值为空将删除用户变量「" + name + "」。继续？", "删除确认"))
                    return false;
            }
            else if (_isNew && EnvVarHelper.FindInScope(_userItems, name) != null)
            {
                if (!ConfirmHelper.Action("用户级已有「" + name + "」，保存将覆盖。继续？", "覆盖确认"))
                    return false;
            }

            if (!EnvVarHelper.TrySetUser(name, deleting ? null : value, out err))
            {
                FormHint.Apply(MsgText, err);
                return false;
            }
            Reload(keepName);
            FormHint.Apply(MsgText, deleting
                ? "已删除用户变量 " + name + "。新开的终端才生效。"
                : "已保存用户变量 " + name + "。新开的终端才生效。");
            return true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!EnvVarHelper.CanEdit(_scope)) return;
            string name = (NameBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                FormHint.Apply(MsgText, "请先选中要删除的变量。");
                return;
            }
            if (!ConfirmHelper.Delete("用户变量 " + name)) return;
            bool wasNew = _isNew;
            if (!wasNew)
            {
                string err;
                if (!EnvVarHelper.TrySetUser(name, null, out err))
                {
                    FormHint.Apply(MsgText, err);
                    return;
                }
            }
            Reload(false);
            FormHint.Apply(MsgText, wasNew
                ? "已取消新建。"
                : "已删除用户变量 " + name + "。新开的终端才生效。");
        }

        private void CopyName_Click(object sender, RoutedEventArgs e)
        {
            CopyText((NameBox.Text ?? "").Trim());
        }

        private void CopyValue_Click(object sender, RoutedEventArgs e)
        {
            CopyText(ValueBox.Text ?? "");
        }

        private void CopyText(string text)
        {
            string error;
            if (!ClipboardHelper.TrySetText(text, out error))
                FormHint.Apply(MsgText, error);
            else
                FormHint.Apply(MsgText, "已复制。");
        }

        private void PathUp_Click(object sender, RoutedEventArgs e)
        {
            MovePath(-1);
        }

        private void PathDown_Click(object sender, RoutedEventArgs e)
        {
            MovePath(1);
        }

        private void MovePath(int delta)
        {
            if (!EnvVarHelper.CanEdit(_scope)) return;
            int i = PathList.SelectedIndex;
            if (!EnvVarHelper.TryMove(_pathParts, i, delta)) return;
            SyncPathToValue();
            PathList.SelectedIndex = i + delta;
        }

        private void PathAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!EnvVarHelper.CanEdit(_scope)) return;
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择要加入 PATH 的目录";
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                {
                    _pathParts.Add(dlg.SelectedPath);
                    SyncPathToValue();
                    PathList.SelectedIndex = _pathParts.Count - 1;
                }
            }
        }

        private void PathDel_Click(object sender, RoutedEventArgs e)
        {
            if (!EnvVarHelper.CanEdit(_scope)) return;
            int i = PathList.SelectedIndex;
            if (i < 0 || i >= _pathParts.Count) return;
            _pathParts.RemoveAt(i);
            SyncPathToValue();
            if (_pathParts.Count > 0)
                PathList.SelectedIndex = i < _pathParts.Count ? i : _pathParts.Count - 1;
        }

        private bool ConfirmDiscardOrSave()
        {
            if (!IsDirty()) return true;
            var r = ConfirmHelper.Unsaved("用户环境变量有未保存的修改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return SaveCurrent();
            return true;
        }
    }
}
