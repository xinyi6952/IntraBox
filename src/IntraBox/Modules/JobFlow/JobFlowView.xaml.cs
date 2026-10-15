using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using IntraBox.Controls;
using IntraBox.Core;

namespace IntraBox.Modules.JobFlow
{
    public partial class JobFlowView : UserControl, IModuleView, ILeaveGuard, IBusyTool
    {
        private readonly ObservableCollection<JobRow> _rows = new ObservableCollection<JobRow>();
        private readonly ObservableCollection<LibRow> _libRows = new ObservableCollection<LibRow>();
        private readonly ObservableCollection<ParamRow> _params = new ObservableCollection<ParamRow>();
        private readonly ObservableCollection<StepRow> _steps = new ObservableCollection<StepRow>();
        private JobItem _current;
        private JobStep _libStep;
        private string _savedJson = "";
        private bool _drawerIsJob = true;
        private bool _libTab;
        private bool _tabSyncing;
        private bool _drawerMax = true;
        private bool _isNew;
        private bool _overflowReadOnly;
        private JobRow _menuRow;
        private LibRow _libMenuRow;
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();
        private const double DrawerRatio = 0.70;

        public bool IsBusy
        {
            get
            {
                if (LoadingOverlay.IsShowing(this)) return true;
                return StepEditor != null && LoadingOverlay.IsShowing(StepEditor);
            }
        }
        public string BusyMessage { get { return "任务编排仍有运行中的操作，离开将中止该操作。确定离开？"; } }

        public JobFlowView()
        {
            InitializeComponent();
            JobGrid.ItemsSource = _rows;
            LibGrid.ItemsSource = _libRows;
            ParamGrid.ItemsSource = _params;
            StepGrid.ItemsSource = _steps;
            JobFilterBar.Attach(JobGrid);
            LibFilterBar.Attach(LibGrid);
            TriggerCombo.SelectedIndex = 0;
            StatusCombo.SelectedIndex = 0;
            WireAutoGrow(RemarksBox);
            WireAutoGrow(LogBox);
            ParamGrid.BeginningEdit += ParamGrid_BeginningEdit;
        }

        public void OnActivated()
        {
            JobFlowStore.Reload();
            JobStepLibStore.Reload();
            ReloadList(null);
            ReloadLib(null);
            SetListMsg(_libTab ? "在步骤库编步骤；REST 可用 curl 识别。" : "从步骤库挑选快照，最多 5 步。");
        }

        public void OnDeactivated()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
            if (StepEditor != null) StepEditor.CancelPending();
        }

        public bool CanLeave() { return CloseDrawer(); }
        public bool HasUnsavedChanges()
        {
            if (DrawerHost == null || DrawerHost.Visibility != Visibility.Visible) return false;
            return IsDirty();
        }

        private bool IsDirty()
        {
            if (DrawerHost.Visibility != Visibility.Visible) return false;
            if (_overflowReadOnly) return false;
            CollectEditor();
            string json;
            if (_drawerIsJob)
            {
                if (_current == null) return false;
                json = Newtonsoft.Json.JsonConvert.SerializeObject(_current);
            }
            else
            {
                if (StepEditor == null || StepEditor.Current == null) return false;
                string err;
                StepEditor.TryCollect(out err);
                json = Newtonsoft.Json.JsonConvert.SerializeObject(StepEditor.Current);
            }
            return json != _savedJson;
        }

        private void SetListMsg(string text)
        {
            var box = _libTab ? LibMsgText : JobMsgText;
            if (box != null) FormHint.Apply(box, text);
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != MainTabs) return;
            if (_tabSyncing) return;
            bool wantLib = MainTabs.SelectedIndex == 1;
            if (!IsLoaded)
            {
                _libTab = wantLib;
                return;
            }
            if (wantLib == _libTab) return;
            if (!CloseDrawer())
            {
                _tabSyncing = true;
                MainTabs.SelectedIndex = _libTab ? 1 : 0;
                _tabSyncing = false;
                return;
            }
            _libTab = wantLib;
            if (_libTab)
            {
                ReloadLib(null);
                if (LibMsgText != null)
                    FormHint.Apply(LibMsgText, "编步骤；REST 可「从 curl 新建」。【示例】只读。", false);
                else
                    SetListMsg("编步骤；REST 可「从 curl 新建」。【示例】只读。");
            }
            else
                SetListMsg("从步骤库挑选快照，最多 5 步。");
        }

        private void JobNewBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenButtonMenu(JobNewBtn);
        }

        private void LibNewBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenButtonMenu(LibNewBtn);
        }

        private static void OpenButtonMenu(Button btn)
        {
            if (btn == null || btn.ContextMenu == null) return;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (!CloseDrawer()) return;
            JobFlowStore.Reload();
            JobStepLibStore.Reload();
            ReloadList(null);
            ReloadLib(null);
            SetListMsg("已刷新。");
        }

        private void ReloadList(string selectUid)
        {
            _rows.Clear();
            var list = JobFlowStore.Snapshot();
            for (int i = 0; i < list.Count; i++)
            {
                if (JobSamples.IsFixedSampleUid(list[i].Uid)) continue;
                _rows.Add(JobRow.From(list[i]));
            }
            if (!string.IsNullOrEmpty(selectUid))
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i].Uid == selectUid) { JobGrid.SelectedIndex = i; return; }
                }
            }
        }

        private void ReloadLib(string selectUid)
        {
            _libRows.Clear();
            var list = JobStepLibStore.Snapshot();
            for (int i = 0; i < list.Count; i++)
                _libRows.Add(LibRow.From(list[i]));
            if (!string.IsNullOrEmpty(selectUid))
            {
                for (int i = 0; i < _libRows.Count; i++)
                {
                    if (_libRows[i].Uid == selectUid) { LibGrid.SelectedIndex = i; return; }
                }
            }
        }

        private void NewBlank_Click(object sender, RoutedEventArgs e)
        {
            if (!CloseDrawer()) return;
            OpenJobDrawer(JobItem.Create("新编排"), true);
            SetListMsg("已新建空白编排，请从步骤库挑选（最多 5 步）。");
        }

        private void LibNewRest_Click(object sender, RoutedEventArgs e) { OpenLibNew(JobStep.Create(JobStepKind.Rest)); }
        private void LibNewSoap_Click(object sender, RoutedEventArgs e) { OpenLibNew(JobStep.Create(JobStepKind.Soap)); }
        private void LibNewUrl_Click(object sender, RoutedEventArgs e) { OpenLibNew(JobStep.Create(JobStepKind.OpenUrl)); }
        private void LibNewProgram_Click(object sender, RoutedEventArgs e) { OpenLibNew(JobStep.Create(JobStepKind.Program)); }
        private void LibNewBat_Click(object sender, RoutedEventArgs e) { OpenLibNew(JobStep.Create(JobStepKind.Bat)); }
        private void LibNewPython_Click(object sender, RoutedEventArgs e) { OpenLibNew(JobStep.Create(JobStepKind.Python)); }

        private void LibNewCurl_Click(object sender, RoutedEventArgs e)
        {
            if (!CloseDrawer()) return;
            var win = new JobCurlPasteWindow();
            win.Owner = Window.GetWindow(this);
            if (win.ShowDialog() != true || win.ResultStep == null) return;
            OpenLibNew(win.ResultStep);
        }

        private void OpenLibNew(JobStep step)
        {
            if (!CloseDrawer()) return;
            OpenLibDrawer(step, true);
        }

        private void JobGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = JobGrid.SelectedItem as JobRow;
            if (row != null) OpenJob(row.Uid);
        }

        private void LibGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = LibGrid.SelectedItem as LibRow;
            if (row != null) OpenLib(row.Uid);
        }

        private void RowOp_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            _menuRow = btn != null ? (btn.Tag as JobRow) : null;
            if (_menuRow == null || btn == null || btn.ContextMenu == null) return;
            bool overflow = false;
            var job = JobFlowStore.Find(_menuRow.Uid);
            if (job != null) overflow = JobLimits.IsStepOverflow(job);
            foreach (var obj in btn.ContextMenu.Items)
            {
                var mi = obj as MenuItem;
                if (mi == null) continue;
                string h = mi.Header as string;
                if (h == "立即运行" || h == "设为草稿" || h == "设为启用" || h == "设为停用")
                    mi.Visibility = overflow && h != "立即运行" ? Visibility.Collapsed : Visibility.Visible;
                if (overflow && (h == "设为草稿" || h == "设为启用" || h == "设为停用"))
                    mi.Visibility = Visibility.Collapsed;
            }
            OpenButtonMenu(btn);
        }

        private void LibOp_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            _libMenuRow = btn != null ? (btn.Tag as LibRow) : null;
            if (_libMenuRow == null || btn == null || btn.ContextMenu == null) return;
            bool sample = JobSamples.IsFixedStepUid(_libMenuRow.Uid);
            foreach (var obj in btn.ContextMenu.Items)
            {
                var mi = obj as MenuItem;
                if (mi == null) continue;
                string h = mi.Header as string;
                if (h == "编辑") mi.Header = sample ? "查看" : "编辑";
                else if (h == "查看") mi.Header = sample ? "查看" : "编辑";
                else if (h == "删除") mi.Visibility = sample ? Visibility.Collapsed : Visibility.Visible;
            }
            OpenButtonMenu(btn);
        }

        private void RowEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_menuRow != null) OpenJob(_menuRow.Uid);
        }

        private void LibEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_libMenuRow != null) OpenLib(_libMenuRow.Uid);
        }

        private async void RowRun_Click(object sender, RoutedEventArgs e)
        {
            if (_menuRow == null) return;
            var job = JobFlowStore.Find(_menuRow.Uid);
            if (job == null) return;
            await RunJobAsync(job, false);
        }

        private void RowDraft_Click(object sender, RoutedEventArgs e) { SetRowStatus(JobStatus.Draft); }
        private void RowEnable_Click(object sender, RoutedEventArgs e) { SetRowStatus(JobStatus.Enabled); }
        private void RowDisable_Click(object sender, RoutedEventArgs e) { SetRowStatus(JobStatus.Disabled); }

        private void SetRowStatus(JobStatus status)
        {
            if (_menuRow == null) return;
            string err;
            if (!JobFlowStore.SetStatus(_menuRow.Uid, status, out err))
            {
                SetListMsg(err);
                return;
            }
            ReloadList(_menuRow.Uid);
            SetListMsg("状态已设为" + JobItem.StatusText(status));
        }

        private void RowHistory_Click(object sender, RoutedEventArgs e)
        {
            if (_menuRow == null) return;
            var win = new JobHistoryWindow(_menuRow.Uid, _menuRow.Name);
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
        }

        private void RowDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_menuRow == null) return;
            if (!ConfirmHelper.Delete(_menuRow.Name)) return;
            if (DrawerHost.Visibility == Visibility.Visible && _current != null && _current.Uid == _menuRow.Uid)
                CloseDrawerDiscard();
            else if (!CloseDrawer()) return;
            string err;
            if (!JobFlowStore.Delete(_menuRow.Uid, out err))
            {
                SetListMsg(err);
                return;
            }
            ReloadList(null);
            SetListMsg("已删除。");
        }

        private void LibDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_libMenuRow == null) return;
            if (!ConfirmHelper.Delete(_libMenuRow.Name)) return;
            string err;
            if (!JobStepLibStore.Delete(_libMenuRow.Uid, out err))
            {
                SetListMsg(err);
                return;
            }
            ReloadLib(null);
            SetListMsg("已删除步骤。");
        }

        private void OpenJob(string uid)
        {
            if (!CloseDrawer()) return;
            var job = JobFlowStore.Find(uid);
            if (job == null) return;
            OpenJobDrawer(job, false);
        }

        private void OpenLib(string uid)
        {
            if (!CloseDrawer()) return;
            var step = JobStepLibStore.Find(uid);
            if (step == null) return;
            OpenLibDrawer(step, false);
        }

        private void OpenJobDrawer(JobItem job, bool isNew)
        {
            _drawerIsJob = true;
            _isNew = isNew;
            _libStep = null;
            _overflowReadOnly = JobLimits.IsStepOverflow(job);
            JobEditHost.Visibility = Visibility.Visible;
            StepEditor.Visibility = Visibility.Collapsed;
            DrawerTitle.Text = isNew ? "新建编排" : (_overflowReadOnly ? "查看编排（只读）" : "编排详情");
            OverflowText.Visibility = _overflowReadOnly ? Visibility.Visible : Visibility.Collapsed;
            LoadJob(job);
            ApplyReadOnly(_overflowReadOnly);
            ShowDrawer();
        }

        private void OpenLibDrawer(JobStep step, bool isNew)
        {
            _drawerIsJob = false;
            _isNew = isNew;
            _current = null;
            _overflowReadOnly = JobSamples.IsFixedStepUid(step.Uid);
            _libStep = step;
            JobEditHost.Visibility = Visibility.Collapsed;
            StepEditor.Visibility = Visibility.Visible;
            DrawerTitle.Text = isNew ? "新建步骤" : (_overflowReadOnly ? "查看步骤（示例只读）" : "步骤详情");
            StepEditor.Load(step, _overflowReadOnly);
            _savedJson = Newtonsoft.Json.JsonConvert.SerializeObject(step);
            ApplyReadOnly(_overflowReadOnly);
            DrawerRun_HideForLib();
            ShowDrawer();
        }

        private void DrawerRun_HideForLib()
        {
            if (DrawerEditButtons == null) return;
            foreach (var obj in DrawerEditButtons.Children)
            {
                var btn = obj as Button;
                if (btn == null) continue;
                string c = btn.Content as string;
                if (c == "立即运行") btn.Visibility = _drawerIsJob ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ApplyReadOnly(bool readOnly)
        {
            bool edit = !readOnly;
            if (NameBox != null) NameBox.IsReadOnly = readOnly;
            if (RemarksBox != null) RemarksBox.IsReadOnly = readOnly;
            if (StatusCombo != null) StatusCombo.IsEnabled = edit;
            if (StepMenuBar != null) StepMenuBar.IsEnabled = edit;
            if (TriggerPanel != null) TriggerPanel.IsEnabled = edit;
            if (ParamGrid != null) ParamGrid.IsReadOnly = readOnly;
            if (DrawerEditButtons != null)
                DrawerEditButtons.Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible;
            if (DrawerViewCloseBtn != null)
                DrawerViewCloseBtn.Visibility = readOnly ? Visibility.Visible : Visibility.Collapsed;
            DrawerRun_HideForLib();
        }

        private void ShowDrawer()
        {
            DrawerHost.Visibility = Visibility.Visible;
            SetDrawerMaximized(true);
            if (DrawerScroll != null) DrawerScroll.ScrollToHome();
            AdjustAutoGrow(RemarksBox);
            AdjustAutoGrow(LogBox);
        }

        private void LoadJob(JobItem job)
        {
            _current = job;
            _params.Clear();
            _steps.Clear();
            if (job == null) return;
            NameBox.Text = job.Name ?? "";
            RemarksBox.Text = job.Remarks ?? "";
            StatusCombo.SelectedIndex = (int)job.Status;
            if (StatusCombo.SelectedIndex < 0) StatusCombo.SelectedIndex = 0;
            MetaText.Text = BuildMeta(job);
            JobHeaderHelper.MergeParams(job);
            if (job.Params != null)
            {
                for (int i = 0; i < job.Params.Count; i++)
                    _params.Add(ParamRow.From(job.Params[i]));
            }
            if (job.Steps != null)
            {
                for (int i = 0; i < job.Steps.Count; i++)
                    _steps.Add(StepRow.From(job.Steps[i]));
            }
            RefreshStepCount();
            LoadTrigger(job.Trigger);
            LogBox.Text = "";
            LoadRunLogAsync(job.Uid);
            _savedJson = Newtonsoft.Json.JsonConvert.SerializeObject(job);
            AdjustAutoGrow(RemarksBox);
            AdjustAutoGrow(LogBox);
        }

        private void CollectEditor()
        {
            if (!_drawerIsJob || _current == null || _overflowReadOnly) return;
            _current.Name = NameBox.Text ?? "";
            _current.Remarks = RemarksBox.Text ?? "";
            int st = StatusCombo.SelectedIndex;
            if (st < 0) st = 0;
            if (st > 2) st = 0;
            _current.Status = (JobStatus)st;
            _current.SyncEnabledFromStatus();
            _current.Params = new List<JobParam>();
            for (int i = 0; i < _params.Count; i++)
                _current.Params.Add(_params[i].ToModel());
            _current.Steps = new List<JobStep>();
            for (int i = 0; i < _steps.Count; i++)
            {
                JobHeaderHelper.Normalize(_steps[i].Step);
                _current.Steps.Add(_steps[i].Step);
            }
            _current.Trigger = CollectTrigger();
            JobHeaderHelper.MergeParams(_current);
        }

        private void RefreshStepCount()
        {
            if (StepCountText != null)
                StepCountText.Text = "步骤 " + _steps.Count + "/" + JobLimits.MaxStepsPerJob;
        }

        private void SyncParamsFromSteps()
        {
            CollectEditor();
            _params.Clear();
            if (_current != null && _current.Params != null)
            {
                for (int i = 0; i < _current.Params.Count; i++)
                    _params.Add(ParamRow.From(_current.Params[i]));
            }
        }

        private void PickStep_Click(object sender, RoutedEventArgs e)
        {
            if (_steps.Count >= JobLimits.MaxStepsPerJob)
            {
                SetDrawerMsg("最多 " + JobLimits.MaxStepsPerJob + " 步。");
                return;
            }
            var win = new JobPickStepWindow();
            win.Owner = Window.GetWindow(this);
            if (win.ShowDialog() != true || win.Picked == null) return;
            var snap = JobHeaderHelper.SnapshotFromLibrary(win.Picked);
            _steps.Add(StepRow.From(snap));
            RefreshStepCount();
            SyncParamsFromSteps();
        }

        private void ReplaceStep_Click(object sender, RoutedEventArgs e)
        {
            int i = StepGrid.SelectedIndex;
            if (i < 0 || i >= _steps.Count)
            {
                SetDrawerMsg("请先选中要替换的步骤。");
                return;
            }
            var win = new JobPickStepWindow();
            win.Owner = Window.GetWindow(this);
            if (win.ShowDialog() != true || win.Picked == null) return;
            _steps[i] = StepRow.From(JobHeaderHelper.SnapshotFromLibrary(win.Picked));
            StepGrid.SelectedIndex = i;
            StepGrid.Items.Refresh();
            SyncParamsFromSteps();
        }

        private void DelStep_Click(object sender, RoutedEventArgs e)
        {
            int i = StepGrid.SelectedIndex;
            if (i < 0 || i >= _steps.Count) return;
            _steps.RemoveAt(i);
            RefreshStepCount();
            SyncParamsFromSteps();
        }

        private void StepUp_Click(object sender, RoutedEventArgs e) { MoveStep(-1); }
        private void StepDown_Click(object sender, RoutedEventArgs e) { MoveStep(1); }

        private void MoveStep(int delta)
        {
            int i = StepGrid.SelectedIndex;
            int n = i + delta;
            if (i < 0 || n < 0 || n >= _steps.Count) return;
            var t = _steps[i];
            _steps[i] = _steps[n];
            _steps[n] = t;
            StepGrid.SelectedIndex = n;
        }

        private void LoadTrigger(JobTrigger t)
        {
            if (t == null) t = JobTrigger.Manual();
            TriggerCombo.SelectedIndex = (int)t.Kind;
            TriggerTimeBox.Text = string.IsNullOrEmpty(t.TimeHHmm) ? "09:00" : t.TimeHHmm;
            IntervalBox.Text = (t.IntervalMinutes < 1 ? 60 : t.IntervalMinutes).ToString();
            WeekdayBox.Text = t.Weekday.ToString();
        }

        private JobTrigger CollectTrigger()
        {
            var t = JobTrigger.Manual();
            int kind = TriggerCombo.SelectedIndex;
            if (kind < 0) kind = 0;
            t.Kind = (JobTriggerKind)kind;
            t.TimeHHmm = (TriggerTimeBox.Text ?? "09:00").Trim();
            int mins;
            if (!int.TryParse(IntervalBox.Text, out mins) || mins < 1) mins = 60;
            t.IntervalMinutes = mins;
            int wd;
            if (!int.TryParse(WeekdayBox.Text, out wd)) wd = 1;
            t.Weekday = wd;
            return t;
        }

        private bool SaveCurrent()
        {
            if (_drawerIsJob)
            {
                if (_current == null) { SetDrawerMsg("没有可保存的编排。"); return false; }
                if (_overflowReadOnly)
                {
                    SetDrawerMsg("步骤超过 5 个，只读不能保存。");
                    return false;
                }
                CollectEditor();
                string err;
                if (!JobFlowStore.Save(_current, out err))
                {
                    SetDrawerMsg(err);
                    return false;
                }
                _isNew = false;
                _savedJson = Newtonsoft.Json.JsonConvert.SerializeObject(_current);
                ReloadList(_current.Uid);
                SetDrawerMsg("已保存。");
                return true;
            }
            string verr;
            if (!StepEditor.TryCollect(out verr))
            {
                SetDrawerMsg(verr);
                return false;
            }
            var step = StepEditor.Current;
            string serr;
            if (!JobStepLibStore.Save(step, out serr))
            {
                SetDrawerMsg(serr);
                return false;
            }
            _isNew = false;
            _savedJson = Newtonsoft.Json.JsonConvert.SerializeObject(step);
            ReloadLib(step.Uid);
            SetDrawerMsg("步骤已保存。编排里已加入的快照不会自动更新，可用「从库替换」。");
            return true;
        }

        private void DrawerSave_Click(object sender, RoutedEventArgs e) { SaveCurrent(); }
        private void DrawerSaveClose_Click(object sender, RoutedEventArgs e)
        {
            if (SaveCurrent()) HideDrawer();
        }

        private async void DrawerRun_Click(object sender, RoutedEventArgs e)
        {
            if (!_drawerIsJob || _current == null) return;
            CollectEditor();
            await RunJobAsync(_current, true);
        }

        private async Task RunJobAsync(JobItem source, bool fromDrawer)
        {
            if (source == null) return;
            if (JobFlowScheduler.IsRunning(source.Uid))
            {
                SetListMsg("该编排正在运行，请稍后再试。");
                return;
            }
            Dictionary<string, string> inputs;
            if (!PromptInputs(source, out inputs)) return;
            if (!JobFlowScheduler.TryBegin(source.Uid))
            {
                SetListMsg("该编排正在运行，请稍后再试。");
                return;
            }
            _gate.Cancel();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            LoadingOverlay.Show(this, "正在运行编排…");
            SetListMsg("运行中…");
            var job = JobFlowStore.Clone(source);
            JobRunResult result = null;
            Exception runEx = null;
            bool cancelled = false;
            try
            {
                result = await Task.Run(() => JobRunner.Run(job, inputs, true, cts.Token), cts.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex) { runEx = ex; }
            finally
            {
                JobFlowScheduler.End(source.Uid);
                if (version == _gate.Version)
                {
                    LoadingOverlay.Hide(this);
                    _gate.DisposeCurrent();
                }
            }
            if (version != _gate.Version) return;
            if (cancelled) { SetListMsg("已取消"); return; }
            if (runEx != null)
            {
                SetListMsg("运行失败：" + runEx.RootMessage());
                if (!fromDrawer) JobResultWindow.ShowException(Window.GetWindow(this), source.Name, runEx);
                return;
            }
            if (result == null)
            {
                SetListMsg("运行失败：无结果");
                if (!fromDrawer)
                    JobResultWindow.ShowException(Window.GetWindow(this), source.Name, new InvalidOperationException("无运行结果"));
                return;
            }
            JobFlowStore.UpdateRunResult(job.Uid, result, false);
            if (fromDrawer && _current != null && _current.Uid == job.Uid)
            {
                _current.ManualSucceeded = job.ManualSucceeded;
                _current.LastRunAt = DateTime.Now;
                _current.LastRunOk = result.Ok;
                _current.LastRunSummary = result.Message;
                _current.RunCount = (_current.RunCount < 0 ? 0 : _current.RunCount) + 1;
                LogBox.Text = string.Join(Environment.NewLine, result.Lines.ToArray());
                MetaText.Text = BuildMeta(_current);
                _savedJson = Newtonsoft.Json.JsonConvert.SerializeObject(_current);
            }
            string doneMsg = result.Ok ? "运行成功（已记入手动成功，允许自动跑）" : "运行失败";
            SetListMsg(doneMsg);
            if (fromDrawer) SetDrawerMsg(doneMsg);
            ReloadList(job.Uid);
            if (!fromDrawer) JobResultWindow.ShowResult(Window.GetWindow(this), job.Name, result);
            GcHelper.CollectAndTrimIdle("jobflow-run");
        }

        private bool PromptInputs(JobItem job, out Dictionary<string, string> inputs)
        {
            inputs = new Dictionary<string, string>(StringComparer.Ordinal);
            if (job.Params == null || job.Params.Count == 0) return true;
            var win = new JobRunWindow(job);
            win.Owner = Window.GetWindow(this);
            if (win.ShowDialog() != true) return false;
            inputs = win.Inputs;
            return true;
        }

        private bool CloseDrawer()
        {
            if (DrawerHost.Visibility != Visibility.Visible) return true;
            if (!IsDirty()) { HideDrawer(); return true; }
            var r = ConfirmHelper.Unsaved("有未保存的修改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes)
            {
                if (!SaveCurrent()) return false;
                HideDrawer();
                return true;
            }
            HideDrawer();
            return true;
        }

        private void CloseDrawerDiscard()
        {
            if (DrawerHost.Visibility != Visibility.Visible) return;
            HideDrawer();
        }

        private void HideDrawer()
        {
            if (StepEditor != null) StepEditor.CancelPending();
            DrawerHost.Visibility = Visibility.Collapsed;
            _current = null;
            _libStep = null;
            _savedJson = "";
        }

        private void ParamGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var row = e.Row != null ? e.Row.Item as ParamRow : null;
            if (row == null) return;
            string header = e.Column != null ? e.Column.Header as string : null;
            if (header == "默认值" && row.TypeText == "密钥")
                e.Cancel = true;
        }

        private void DrawerClose_Click(object sender, RoutedEventArgs e) { CloseDrawer(); }
        private void DrawerMask_Click(object sender, MouseButtonEventArgs e) { CloseDrawer(); }
        private void DrawerMax_Click(object sender, RoutedEventArgs e) { SetDrawerMaximized(!_drawerMax); }

        private void SetDrawerMaximized(bool on)
        {
            _drawerMax = on;
            if (IconExpand != null) IconExpand.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (IconRestore != null) IconRestore.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            ApplyDrawerSize();
        }

        private void ApplyDrawerSize()
        {
            if (DrawerPanel == null || RootGrid == null) return;
            double w = RootGrid.ActualWidth;
            if (w < 100) return;
            DrawerPanel.Width = _drawerMax ? w : Math.Max(360, w * DrawerRatio);
        }

        private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DrawerHost != null && DrawerHost.Visibility == Visibility.Visible)
                ApplyDrawerSize();
        }

        private void DrawerScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (DrawerScroll == null) return;
            DrawerScroll.ScrollToVerticalOffset(DrawerScroll.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void SetDrawerMsg(string text)
        {
            if (DrawerMsg != null) FormHint.Apply(DrawerMsg, text ?? "");
        }

        private static string BuildMeta(JobItem job)
        {
            if (job == null) return "";
            return (job.ManualSucceeded ? "已手动成功" : "尚未手动成功")
                + (job.LastRunAt.HasValue
                    ? " · 上次 " + job.LastRunAt.Value.ToString("MM-dd HH:mm") + (job.LastRunOk ? " 成功" : " 失败")
                    : "")
                + " · 执行 " + job.RunCount + " 次";
        }

        private async void LoadRunLogAsync(string uid)
        {
            string logPath = JobFlowStore.RunLogPath(uid);
            bool heavy = false;
            try
            {
                heavy = File.Exists(logPath) && new FileInfo(logPath).Length > JobRunLogHelper.LogTailBytes;
            }
            catch (Exception ex)
            {
                SetDrawerMsg("读取运行日志失败：" + ex.Message);
                return;
            }
            JobLogRead read;
            if (heavy)
            {
                LoadingOverlay.Show(this, "正在读取运行日志…");
                try
                {
                    read = await Task.Run(() => JobRunLogHelper.ReadTail(logPath));
                }
                finally
                {
                    LoadingOverlay.Hide(this);
                }
            }
            else
                read = JobRunLogHelper.ReadTail(logPath);
            if (!IsLoaded || _current == null || _current.Uid != uid || LogBox == null) return;
            if (!string.IsNullOrEmpty(read.Error))
            {
                LogBox.Text = "";
                SetDrawerMsg("读取运行日志失败：" + read.Error);
                return;
            }
            LogBox.Text = JobRunLogHelper.ClipDisplay(read.Text, JobRunLogHelper.DisplayChars);
        }

        private void WireAutoGrow(TextBox box)
        {
            if (box == null) return;
            box.TextChanged += (s, e) => AdjustAutoGrow(box);
            box.SizeChanged += (s, e) => { if (e.WidthChanged) AdjustAutoGrow(box); };
        }

        private static void AdjustAutoGrow(TextBox box)
        {
            if (box == null) return;
            try
            {
                double minH = !double.IsNaN(box.MinHeight) && box.MinHeight > 0 ? box.MinHeight : 56;
                double maxH = !double.IsInfinity(box.MaxHeight) && box.MaxHeight > 0 ? box.MaxHeight : 320;
                box.UpdateLayout();
                double width = box.ActualWidth < 40 ? 480 : box.ActualWidth;
                var ft = new System.Windows.Media.FormattedText(
                    string.IsNullOrEmpty(box.Text) ? " ag" : box.Text,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch),
                    box.FontSize, System.Windows.Media.Brushes.Black,
                    WinCompatHelper.PixelsPerDip(box));
                ft.MaxTextWidth = Math.Max(40, width - 24);
                double contentH = string.IsNullOrEmpty(box.Text) ? (box.FontSize * 1.35) : ft.Height;
                double h = contentH + 16;
                if (h < minH) h = minH;
                if (h > maxH) h = maxH;
                box.Height = h;
            }
            catch { }
        }

        private sealed class JobRow
        {
            public string Uid { get; set; }
            public string Name { get; set; }
            public string StatusText { get; set; }
            public string TriggerText { get; set; }
            public string TestPassText { get; set; }
            public int RunCount { get; set; }
            public string LastRunText { get; set; }
            public string LastOkText { get; set; }

            public static JobRow From(JobItem j)
            {
                return new JobRow
                {
                    Uid = j.Uid,
                    Name = j.Name,
                    StatusText = JobItem.StatusText(j.Status),
                    TriggerText = JobScheduleHelper.FormatTriggerText(j.Trigger),
                    TestPassText = j.TestPassAt.HasValue ? j.TestPassAt.Value.ToString("yyyy-MM-dd HH:mm") : "",
                    RunCount = j.RunCount,
                    LastRunText = j.LastRunAt.HasValue ? j.LastRunAt.Value.ToString("yyyy-MM-dd HH:mm") : "",
                    LastOkText = j.LastRunAt.HasValue ? (j.LastRunOk ? "成功" : "失败") : "—"
                };
            }
        }

        private sealed class LibRow
        {
            public string Uid { get; set; }
            public string Name { get; set; }
            public string KindText { get; set; }

            public static LibRow From(JobStep s)
            {
                return new LibRow
                {
                    Uid = s.Uid,
                    Name = s.Name,
                    KindText = JobStep.KindText(s.Kind)
                };
            }
        }

        private sealed class ParamRow
        {
            public string Name { get; set; }
            public string DisplayName { get; set; }
            public string TypeText { get; set; }
            public bool Required { get; set; }
            public string DefaultValue { get; set; }

            public static ParamRow From(JobParam p)
            {
                return new ParamRow
                {
                    Name = p.Name,
                    DisplayName = p.DisplayName,
                    TypeText = TypeToText(p.Type),
                    Required = p.Required,
                    DefaultValue = p.DefaultValue
                };
            }

            public JobParam ToModel()
            {
                return new JobParam
                {
                    Name = Name,
                    DisplayName = DisplayName,
                    Type = TextToType(TypeText),
                    Required = Required,
                    DefaultValue = TextToType(TypeText) == JobParamType.Secret ? "" : DefaultValue
                };
            }

            private static string TypeToText(JobParamType t)
            {
                if (t == JobParamType.Number) return "数字";
                if (t == JobParamType.Bool) return "是否";
                if (t == JobParamType.Secret) return "密钥";
                return "文本";
            }

            private static JobParamType TextToType(string t)
            {
                if (t == "数字") return JobParamType.Number;
                if (t == "是否") return JobParamType.Bool;
                if (t == "密钥") return JobParamType.Secret;
                return JobParamType.Text;
            }
        }

        private sealed class StepRow : INotifyPropertyChanged
        {
            public JobStep Step { get; set; }
            public string Name { get; set; }
            public string KindText { get; set; }
            public string LibraryText { get; set; }
#pragma warning disable 0067
            public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore 0067

            public static StepRow From(JobStep s)
            {
                var r = new StepRow { Step = s };
                r.Name = s != null ? s.Name : "";
                r.KindText = JobStep.KindText(s != null ? s.Kind : JobStepKind.Rest);
                r.LibraryText = s != null && !string.IsNullOrEmpty(s.LibraryUid) ? s.LibraryUid : "（快照）";
                if (s != null && JobSamples.IsFixedStepUid(s.LibraryUid))
                    r.LibraryText = "示例";
                return r;
            }
        }
    }
}
