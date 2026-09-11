using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.Vault
{
    public partial class VaultEditorPanel : UserControl
    {
        private bool _loading;
        private bool _dirty;
        private bool _readOnly;
        private VaultItem _item;
        private VaultBody _body;
        private bool _revealed;
        private byte[] _key;
        private readonly ObservableCollection<VaultRowVm> _rows = new ObservableCollection<VaultRowVm>();
        private DispatcherTimer _saveTimer;

        public event EventHandler Saved;
        public event EventHandler CloseRequested;

        public VaultEditorPanel()
        {
            _loading = true;
            InitializeComponent();
            RowList.ItemsSource = _rows;
            _saveTimer = new DispatcherTimer();
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); Persist(false, false); };
            Unloaded += (s, e) =>
            {
                if (_saveTimer != null) _saveTimer.Stop();
            };
            try
            {
                AutoSaveCheck.IsChecked = ConfigManager.Instance.Settings.VaultAutoSave;
            }
            catch
            {
                AutoSaveCheck.IsChecked = false;
            }
            _loading = false;
        }

        public string CurrentUid
        {
            get { return _item != null ? _item.Uid : null; }
        }

        public bool AutoSaveChecked
        {
            get { return AutoSaveCheck != null && AutoSaveCheck.IsChecked == true; }
        }

        public bool AutoSaveEnabled
        {
            get { return AutoSaveChecked && !IsReadOnly; }
        }

        public bool IsReadOnly
        {
            get { return _readOnly; }
        }

        public bool IsDirty()
        {
            return _dirty;
        }

        public void FlushNow()
        {
            Persist(true);
        }

        /// <summary>托盘静默卸载：落盘失败只写状态、不弹框。</summary>
        public void FlushSilent()
        {
            if (_saveTimer != null) _saveTimer.Stop();
            Persist(false, false);
        }

        public void LoadItem(VaultItem item)
        {
            _loading = true;
            _item = item;
            _body = item != null ? VaultStore.LoadBody(item.Uid) : VaultStore.NewEmptyBody();
            if (_body.Entries == null) _body.Entries = new List<VaultEntry>();
            TitleBox.Text = item != null ? (item.Title ?? "") : "";
            RemarkBox.Text = _body.Remark ?? "";
            _revealed = false;
            _key = null;
            RebuildRows();
            _dirty = false;
            _readOnly = item != null && item.ReadOnly;
            if (ReadOnlyCheck != null) ReadOnlyCheck.IsChecked = _readOnly;
            _loading = false;
            ApplyEditorLock();
            UpdatePwdUi();
            UpdateSaveHint();
        }

        public void ForgetSession()
        {
            _key = null;
            _revealed = false;
        }

        private void RebuildRows()
        {
            _rows.Clear();
            if (_body != null && _body.Entries != null)
            {
                for (int i = 0; i < _body.Entries.Count; i++)
                    _rows.Add(VaultRowVm.From(_body.Entries[i], _revealed));
            }
            if (_rows.Count == 0)
                _rows.Add(VaultRowVm.Blank(_revealed));
        }

        private void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (_readOnly) return;
            Persist(true);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_readOnly) return;
            Persist(true);
        }

        private void SaveClose_Click(object sender, RoutedEventArgs e)
        {
            if (_readOnly)
            {
                if (CloseRequested != null) CloseRequested(this, EventArgs.Empty);
                return;
            }
            if (!Persist(true)) return;
            if (CloseRequested != null) CloseRequested(this, EventArgs.Empty);
        }

        private void Panel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (!_readOnly) Persist(true);
                e.Handled = true;
            }
        }

        private void Field_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            MarkDirty();
        }

        private void RowField_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            MarkDirty();
        }

        private void AutoSave_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = AutoSaveChecked;
            try
            {
                ConfigManager.Instance.Settings.VaultAutoSave = on;
                ConfigManager.Instance.Save();
            }
            catch { }
            if (on && !_readOnly)
                Persist(true);
            else if (_saveTimer != null)
                _saveTimer.Stop();
            UpdateSaveHint();
        }

        private void ReadOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || _item == null) return;
            bool next = ReadOnlyCheck != null && ReadOnlyCheck.IsChecked == true;
            if (next && !_readOnly)
                Persist(true);
            _readOnly = next;
            _item.ReadOnly = _readOnly;
            VaultStore.SetReadOnly(_item.Uid, _readOnly);
            ApplyEditorLock();
            if (_readOnly)
            {
                if (_saveTimer != null) _saveTimer.Stop();
            }
            else if (AutoSaveChecked)
                Persist(true);
            UpdateSaveHint();
            if (Saved != null) Saved(this, EventArgs.Empty);
        }

        private void ApplyEditorLock()
        {
            bool edit = !_readOnly;
            if (TitleBox != null) TitleBox.IsReadOnly = _readOnly;
            if (RemarkBox != null) RemarkBox.IsReadOnly = _readOnly;
            if (AddRowBtn != null) AddRowBtn.IsEnabled = edit;
            if (ImportBtn != null) ImportBtn.IsEnabled = edit;
            if (PwdBtn != null) PwdBtn.IsEnabled = edit;
            if (SaveBtn != null) SaveBtn.IsEnabled = edit;
            if (SaveCloseBtn != null) SaveCloseBtn.IsEnabled = edit;
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].EditorLocked = _readOnly;
                _rows[i].RaiseLock();
            }
        }

        private void MarkDirty()
        {
            if (_readOnly) return;
            _dirty = true;
            UpdateSaveHint();
            KickSave();
        }

        private void KickSave()
        {
            if (!AutoSaveEnabled) return;
            _saveTimer.Stop();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(AppSettings.CurrentHistoryPersistDelayMs());
            _saveTimer.Start();
        }

        private bool Persist(bool showOk)
        {
            return Persist(showOk, true);
        }

        private bool Persist(bool showOk, bool alertOnError)
        {
            if (_item == null) return false;
            if (_readOnly) return true;
            CollectToBody();
            byte[] key = _key;
            _item.Title = (TitleBox.Text ?? "").Trim();
            if (_item.Title.Length == 0)
                _item.Title = VaultStore.UniqueTitle(_item.Uid);
            TitleBox.Text = _item.Title;
            string err = VaultStore.SaveBody(_item, _body, key);
            if (err != null)
            {
                SetStatus(err);
                if (alertOnError)
                    MessageBox.Show(err, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _dirty = false;
            UpdateSaveHint();
            if (showOk) SetStatus("已保存");
            if (Saved != null) Saved(this, EventArgs.Empty);
            return true;
        }

        private void CollectToBody()
        {
            if (_body == null) _body = VaultStore.NewEmptyBody();
            _body.Remark = RemarkBox.Text ?? "";
            var list = new List<VaultEntry>();
            for (int i = 0; i < _rows.Count; i++)
                list.Add(_rows[i].ToEntry());
            _body.Entries = list;
        }

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            if (_readOnly) return;
            ClearDeleteConfirm(null);
            _rows.Add(VaultRowVm.Blank(_revealed));
            MarkDirty();
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            string text, err;
            if (!TryBuildPlaintext(out text, out err))
            {
                if (!string.IsNullOrEmpty(err)) SetStatus(err);
                return;
            }
            if (!VaultClipboard.TryCopy(text, out err))
            {
                SetStatus(err ?? "复制失败");
                return;
            }
            SetStatus("已复制全部账号密码");
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            string text, err;
            if (!TryBuildPlaintext(out text, out err))
            {
                if (!string.IsNullOrEmpty(err)) SetStatus(err);
                return;
            }
            var dlg = new SaveFileDialog
            {
                Filter = "文本|*.txt|所有文件|*.*",
                FileName = VaultText.SafeFileName(TitleBox != null ? TitleBox.Text : null) + ".txt"
            };
            if (dlg.ShowDialog(OwnerWin()) != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, text, new UTF8Encoding(true));
                SetStatus("已导出");
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_readOnly) return;
            var dlg = new OpenFileDialog { Filter = "文本|*.txt|所有文件|*.*" };
            if (dlg.ShowDialog(OwnerWin()) != true) return;
            string check;
            if (!SizeLimits.TryCheckFile(dlg.FileName, out check))
            {
                MessageBox.Show(check, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string text;
            try
            {
                text = File.ReadAllText(dlg.FileName, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取失败：" + ex.Message, "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int n = ImportEntries(VaultText.ParseExport(text));
            if (n == 0) SetStatus("没有可导入的账号密码");
            else SetStatus("已导入 " + n + " 条");
        }

        public bool TryBuildPlaintext(out string text, out string error)
        {
            text = null;
            error = null;
            if (HasMaskedRow() && !EnsureVerified(false))
            {
                error = "加密行需先验证查看密码";
                return false;
            }
            var list = new List<VaultEntry>();
            for (int i = 0; i < _rows.Count; i++)
                list.Add(_rows[i].ToEntry());
            text = VaultText.CopyAll(list);
            if (string.IsNullOrEmpty(text))
            {
                error = "没有可复制的账号密码";
                return false;
            }
            return true;
        }

        public int ImportEntries(IList<VaultEntry> entries)
        {
            if (_readOnly) return 0;
            if (entries == null || entries.Count == 0) return 0;
            if (_rows.Count == 1 && !VaultText.IsKept(_rows[0].ToEntry()))
                _rows.Clear();
            int n = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                e.Masked = false;
                e.AccountEnc = null;
                e.PasswordEnc = null;
                if (string.IsNullOrEmpty(e.Id)) e.Id = Guid.NewGuid().ToString("N");
                _rows.Add(VaultRowVm.From(e, _revealed));
                n++;
            }
            if (_rows.Count == 0)
                _rows.Add(VaultRowVm.Blank(_revealed));
            if (n > 0) MarkDirty();
            return n;
        }

        public string CurrentTitle
        {
            get { return TitleBox != null ? TitleBox.Text : null; }
        }

        private bool HasMaskedRow()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Masked) return true;
            }
            return false;
        }

        private void DelRow_Click(object sender, RoutedEventArgs e)
        {
            if (_readOnly) return;
            var row = RowFromSender(sender);
            if (row == null) return;
            if (!row.ConfirmingDelete)
            {
                ClearDeleteConfirm(row);
                row.ConfirmingDelete = true;
                row.RaiseDeleteConfirm();
                SetStatus("再点一次确认删除");
                return;
            }
            _rows.Remove(row);
            if (_rows.Count == 0)
                _rows.Add(VaultRowVm.Blank(_revealed));
            MarkDirty();
            SetStatus("已删除该行");
        }

        private void ClearDeleteConfirm(VaultRowVm keep)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                if (r == keep || !r.ConfirmingDelete) continue;
                r.ConfirmingDelete = false;
                r.RaiseDeleteConfirm();
            }
        }

        private void Mask_Click(object sender, RoutedEventArgs e)
        {
            if (_loading || _readOnly) return;
            var box = sender as CheckBox;
            var row = box != null ? box.DataContext as VaultRowVm : null;
            if (row == null) return;
            if (box.IsChecked == true)
            {
                if (!EnsureVerified(true))
                {
                    RevertMask(box, row, false);
                    return;
                }
                row.Masked = true;
                SealRow(row);
                row.RaiseLock();
                MarkDirty();
                SetStatus("已加密该行");
                return;
            }
            if (!EnsureVerified(false))
            {
                RevertMask(box, row, true);
                return;
            }
            row = FindRow(row.Id) ?? row;
            row.Masked = false;
            row.AccountEnc = null;
            row.PasswordEnc = null;
            row.RaiseLock();
            MarkDirty();
            SetStatus("已改为明文");
        }

        private void RevertMask(CheckBox box, VaultRowVm row, bool masked)
        {
            _loading = true;
            row.Masked = masked;
            box.IsChecked = masked;
            _loading = false;
            row.RaiseLock();
        }

        private bool EnsureVerified(bool forEncrypt)
        {
            if (_revealed && _key != null) return true;
            if (_body != null && _body.HasPassword)
                return TryVerifyPassword();
            if (forEncrypt)
                return TrySetPassword();
            SetStatus("尚未设置查看密码");
            return false;
        }

        private void Password_Click(object sender, RoutedEventArgs e)
        {
            if (_readOnly) return;
            if (_body != null && _body.HasPassword)
                TryChangePassword();
            else
                TrySetPassword();
        }

        private bool TryVerifyPassword()
        {
            if (_body == null || !_body.HasPassword)
            {
                SetStatus("尚未设置查看密码");
                return false;
            }
            byte[] key;
            if (!VaultPasswordWindow.TryUnlock(OwnerWin(), _body.SaltBytes(), _body.Verifier, out key))
                return false;
            string err;
            if (!RevealAllRows(key, out err))
            {
                MessageBox.Show(err ?? "验证失败", "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            UpdatePwdUi();
            SetStatus("已验证查看密码");
            return true;
        }

        private bool RevealAllRows(byte[] key, out string error)
        {
            error = null;
            if (key == null)
            {
                error = "请先验证查看密码";
                return false;
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                string keepAcc = row.Account;
                string keepPwd = row.Password;
                var e = row.ToEntry();
                if (!VaultText.TryReveal(e, key, out error))
                    return false;
                if (string.IsNullOrEmpty(keepAcc))
                    row.Account = e.Account ?? "";
                if (string.IsNullOrEmpty(keepPwd))
                    row.Password = e.Password ?? "";
                row.Reveal = true;
                row.RaiseLock();
            }
            _key = key;
            _revealed = true;
            return true;
        }

        private void SealRow(VaultRowVm row)
        {
            if (row == null || _key == null) return;
            var e = row.ToEntry();
            e.Masked = true;
            VaultText.SealFields(e, _key);
            row.AccountEnc = e.AccountEnc;
            row.PasswordEnc = e.PasswordEnc;
        }

        private void SealMaskedRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Masked)
                    SealRow(_rows[i]);
            }
        }

        private bool TrySetPassword()
        {
            byte[] salt, key;
            string verifier;
            if (!VaultPasswordWindow.TrySetNew(OwnerWin(), out salt, out key, out verifier))
                return false;
            if (_body == null) _body = VaultStore.NewEmptyBody();
            VaultStore.ApplyPassword(_body, salt, key);
            _key = key;
            _revealed = true;
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].Reveal = true;
                _rows[i].RaiseLock();
            }
            SealMaskedRows();
            UpdatePwdUi();
            MarkDirty();
            SetStatus("已设置查看密码");
            return true;
        }

        private void TryChangePassword()
        {
            if (_body == null || !_body.HasPassword) return;
            byte[] salt, key, oldKey;
            string verifier;
            if (!VaultPasswordWindow.TryChange(OwnerWin(), _body.SaltBytes(), _body.Verifier,
                    out salt, out key, out verifier, out oldKey))
                return;
            if (!_revealed)
            {
                string err;
                if (!RevealAllRows(oldKey ?? _key, out err))
                {
                    MessageBox.Show(err ?? "验证失败", "IntraBox", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            VaultStore.ApplyPassword(_body, salt, key);
            _key = key;
            _revealed = true;
            SealMaskedRows();
            UpdatePwdUi();
            MarkDirty();
            Persist(true);
            SetStatus("已修改查看密码");
        }

        private void SecretCell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var fe = sender as FrameworkElement;
            var row = fe != null ? fe.DataContext as VaultRowVm : null;
            if (row == null || !row.Masked) return;
            SetStatus("取消勾选「加密」后可查看并编辑明文");
        }

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.ContextMenu == null) return;
            btn.ContextMenu.DataContext = btn.DataContext;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }

        private void CopyAp_Click(object sender, RoutedEventArgs e)
        {
            CopyRow(RowFromSender(sender), false, true, true);
        }

        private void CopyPap_Click(object sender, RoutedEventArgs e)
        {
            CopyRow(RowFromSender(sender), true, true, true);
        }

        private void CopyA_Click(object sender, RoutedEventArgs e)
        {
            CopyRow(RowFromSender(sender), false, true, false);
        }

        private void CopyP_Click(object sender, RoutedEventArgs e)
        {
            CopyRow(RowFromSender(sender), false, false, true);
        }

        private void CopyNewRow_Click(object sender, RoutedEventArgs e)
        {
            if (_readOnly) return;
            var src = RowFromSender(sender);
            if (src == null) return;
            ClearDeleteConfirm(null);
            var neu = VaultRowVm.Blank(_revealed);
            neu.PlatformsText = src.PlatformsText ?? "";
            neu.Url = src.Url ?? "";
            neu.Account = "";
            neu.Password = "";
            neu.Masked = false;
            int idx = _rows.IndexOf(src);
            if (idx >= 0) _rows.Insert(idx + 1, neu);
            else _rows.Add(neu);
            MarkDirty();
            SetStatus("已复制新增一行");
        }

        private void CopyRow(VaultRowVm row, bool withPlat, bool acc, bool pwd)
        {
            if (row == null) return;
            if (row.Masked && !_revealed)
            {
                if (!EnsureVerified(false))
                {
                    SetStatus("加密行需先验证查看密码才能复制");
                    return;
                }
                row = FindRow(row.Id);
                if (row == null) return;
            }
            string text;
            if (withPlat || (acc && pwd))
                text = VaultText.CopyRecord(VaultText.ParsePlatforms(row.PlatformsText), row.Url, row.Account, row.Password);
            else if (acc)
                text = row.Account ?? "";
            else
                text = row.Password ?? "";
            string err;
            if (!VaultClipboard.TryCopy(text, out err))
            {
                SetStatus(err ?? "复制失败");
                return;
            }
            if (withPlat) SetStatus("已复制平台与账号密码");
            else if (acc && pwd) SetStatus("已复制账号密码");
            else if (acc) SetStatus("已复制账号");
            else SetStatus("已复制密码");
        }

        private VaultRowVm FindRow(string id)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Id == id) return _rows[i];
            }
            return null;
        }

        private static VaultRowVm RowFromSender(object sender)
        {
            var fe = sender as FrameworkElement;
            if (fe == null) return null;
            var row = fe.DataContext as VaultRowVm;
            if (row != null) return row;
            return fe.Tag as VaultRowVm;
        }

        private void UpdatePwdUi()
        {
            bool hasPwd = _body != null && _body.HasPassword;
            if (PwdBtn != null)
                PwdBtn.Content = hasPwd ? "修改查看密码" : "设置查看密码";
            if (LockHint != null)
            {
                if (!hasPwd) LockHint.Text = "未勾选加密的行明文保存";
                else if (_revealed) LockHint.Text = "已验证查看密码";
                else LockHint.Text = "加密行需验证查看密码后才能查看";
            }
        }

        private void UpdateSaveHint()
        {
            if (SaveHint == null) return;
            if (_readOnly) SaveHint.Text = "锁定";
            else SaveHint.Text = _dirty ? "未保存" : "已保存";
        }

        private void SetStatus(string text)
        {
            if (StatusText != null)
                StatusText.Text = text;
        }

        private Window OwnerWin()
        {
            return Window.GetWindow(this) ?? (Application.Current != null ? Application.Current.MainWindow : null);
        }
    }

    public sealed class VaultRowVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Id { get; set; }
        public string PlatformsText { get; set; }
        public string Url { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string AccountEnc { get; set; }
        public string PasswordEnc { get; set; }
        public bool Masked { get; set; }
        public bool Reveal { get; set; }
        public bool ConfirmingDelete { get; set; }
        public bool EditorLocked { get; set; }

        public bool CanEditRow
        {
            get { return !EditorLocked; }
        }

        public bool SecretsEditable
        {
            get { return !Masked && !EditorLocked; }
        }

        public string AccountText
        {
            get { return SecretsEditable ? (Account ?? "") : "••••••••"; }
            set
            {
                if (!SecretsEditable) return;
                if (value == "••••••••") return;
                Account = value;
            }
        }

        public string PasswordText
        {
            get { return SecretsEditable ? (Password ?? "") : "••••••••"; }
            set
            {
                if (!SecretsEditable) return;
                if (value == "••••••••") return;
                Password = value;
            }
        }

        public void RaiseDeleteConfirm()
        {
            Raise("ConfirmingDelete");
        }

        public void RaiseLock()
        {
            Raise("SecretsEditable");
            Raise("AccountText");
            Raise("PasswordText");
            Raise("Masked");
            Raise("EditorLocked");
            Raise("CanEditRow");
        }

        public VaultEntry ToEntry()
        {
            return new VaultEntry
            {
                Id = Id,
                Platforms = VaultText.ParsePlatforms(PlatformsText),
                Url = Url,
                Account = Account,
                Password = Password,
                Masked = Masked,
                AccountEnc = AccountEnc,
                PasswordEnc = PasswordEnc
            };
        }

        public static VaultRowVm Blank(bool reveal)
        {
            return new VaultRowVm
            {
                Id = Guid.NewGuid().ToString("N"),
                PlatformsText = "",
                Url = "",
                Account = "",
                Password = "",
                Reveal = reveal
            };
        }

        public static VaultRowVm From(VaultEntry e, bool reveal)
        {
            if (e == null) return Blank(reveal);
            VaultText.NormalizeEntry(e);
            return new VaultRowVm
            {
                Id = e.Id,
                PlatformsText = VaultText.FormatPlatforms(e.Platforms),
                Url = e.Url ?? "",
                Account = e.Account ?? "",
                Password = e.Password ?? "",
                AccountEnc = e.AccountEnc,
                PasswordEnc = e.PasswordEnc,
                Masked = e.Masked,
                Reveal = reveal
            };
        }

        private void Raise(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
