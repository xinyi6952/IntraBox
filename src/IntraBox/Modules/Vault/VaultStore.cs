using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using IntraBox.Core;
using Newtonsoft.Json;

namespace IntraBox.Modules.Vault
{
    public static class VaultStore
    {
        public const int IndexVersion = 1;
        public const int BodyVersion = 1;

        private static readonly object _sync = new object();
        private static List<VaultItem> _items = new List<VaultItem>();
        private static Timer _diskTimer;
        private static int _inflight;
        private static bool _loaded;
        private static bool _sampleSeeded;

        public static void Reload()
        {
            lock (_sync)
            {
                LoadLocked();
            }
        }

        public static void Flush()
        {
            lock (_sync)
            {
                if (!_loaded) return;
                try { WriteLocked(); }
                catch { }
            }
        }

        public static List<VaultItem> Snapshot()
        {
            EnsureLoaded();
            lock (_sync)
            {
                var list = new List<VaultItem>(_items.Count);
                for (int i = 0; i < _items.Count; i++)
                    list.Add(Clone(_items[i]));
                return list;
            }
        }

        public static VaultItem GetByUid(string uid)
        {
            EnsureLoaded();
            lock (_sync)
            {
                var it = FindLocked(uid);
                return it == null ? null : Clone(it);
            }
        }

        public static VaultItem Add(VaultItem item)
        {
            if (item == null) return null;
            EnsureLoaded();
            lock (_sync)
            {
                Normalize(item);
                Directory.CreateDirectory(DataPaths.VaultItemDir(item.Uid));
                WriteBodyFileLocked(item.Uid, NewEmptyBody());
                _items.Insert(0, Clone(item));
                SchedulePersistLocked();
                return Clone(item);
            }
        }

        public static void UpsertMeta(VaultItem item)
        {
            if (item == null) return;
            EnsureLoaded();
            lock (_sync)
            {
                Normalize(item);
                int i = IndexOfLocked(item.Uid);
                if (i < 0)
                    _items.Insert(0, Clone(item));
                else
                    _items[i] = Clone(item);
                SchedulePersistLocked();
            }
        }

        public static void SetPinned(string uid, bool pinned)
        {
            EnsureLoaded();
            lock (_sync)
            {
                var it = FindLocked(uid);
                if (it == null || it.ReadOnly) return;
                it.Pinned = pinned;
                it.UpdatedAt = DateTime.Now;
                SchedulePersistLocked();
            }
        }

        public static void SetReadOnly(string uid, bool readOnly)
        {
            EnsureLoaded();
            lock (_sync)
            {
                var it = FindLocked(uid);
                if (it == null) return;
                it.ReadOnly = readOnly;
                SchedulePersistLocked();
            }
        }

        public static void Delete(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            EnsureLoaded();
            lock (_sync)
            {
                int i = IndexOfLocked(uid);
                if (i < 0) return;
                if (_items[i].ReadOnly) return;
                _items.RemoveAt(i);
                try
                {
                    string dir = DataPaths.VaultItemDir(uid);
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch { }
                SchedulePersistLocked();
            }
        }

        public static string UniqueTitle(string exceptUid)
        {
            EnsureLoaded();
            lock (_sync)
            {
                string baseTitle = VaultText.DefaultTitle;
                if (!TitleExistsLocked(baseTitle, exceptUid)) return baseTitle;
                for (int n = 2; n < 1000; n++)
                {
                    string t = baseTitle + " (" + n + ")";
                    if (!TitleExistsLocked(t, exceptUid)) return t;
                }
                return baseTitle + " (" + Guid.NewGuid().ToString("N").Substring(0, 6) + ")";
            }
        }

        public static VaultBody LoadBody(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return NewEmptyBody();
            EnsureLoaded();
            lock (_sync)
            {
                return ReadBodyFileLocked(uid);
            }
        }

        public static string SaveBody(VaultItem meta, VaultBody body, byte[] key)
        {
            if (meta == null) return "分类无效";
            if (meta.ReadOnly) return "已锁定，不能保存";
            EnsureLoaded();
            lock (_sync)
            {
                var existing = FindLocked(meta.Uid);
                if (existing != null && existing.ReadOnly)
                    return "已锁定，不能保存";
            }
            if (body == null) body = NewEmptyBody();
            var kept = new List<VaultEntry>();
            if (body.Entries != null)
            {
                for (int i = 0; i < body.Entries.Count; i++)
                {
                    var e = CloneEntry(body.Entries[i]);
                    if (!VaultText.IsKept(e)) continue;
                    string err = VaultText.ValidateEntry(e);
                    if (err != null) return err;
                    if (e.Masked && key == null && string.IsNullOrEmpty(e.AccountEnc))
                        return "加密行需要先验证或设置查看密码";
                    try
                    {
                        if (e.Masked)
                        {
                            if (key != null)
                                VaultText.PackForDisk(e, key);
                        }
                        else
                        {
                            e.AccountEnc = null;
                            e.PasswordEnc = null;
                            VaultText.NormalizeEntry(e);
                        }
                    }
                    catch (Exception ex)
                    {
                        return ex.Message;
                    }
                    kept.Add(e);
                }
            }
            if (VaultText.HasMasked(kept) && (key == null && string.IsNullOrEmpty(body.Verifier)))
                return "加密行需要先设置查看密码";

            body.Version = BodyVersion;
            var disk = new VaultBody
            {
                Version = BodyVersion,
                Remark = body.Remark,
                Salt = body.Salt,
                Verifier = body.Verifier,
                Entries = kept
            };
            meta.EntryCount = kept.Count;
            meta.HasSecret = VaultText.HasMasked(kept) || disk.HasPassword;
            meta.UpdatedAt = DateTime.Now;
            if (string.IsNullOrEmpty(meta.Title))
                meta.Title = VaultText.DefaultTitle;

            EnsureLoaded();
            lock (_sync)
            {
                Normalize(meta);
                Directory.CreateDirectory(DataPaths.VaultItemDir(meta.Uid));
                WriteBodyFileLocked(meta.Uid, disk);
                int i = IndexOfLocked(meta.Uid);
                if (i < 0)
                    _items.Insert(0, Clone(meta));
                else
                    _items[i] = Clone(meta);
                SchedulePersistLocked();
            }
            return null;
        }

        public static VaultBody NewEmptyBody()
        {
            return new VaultBody
            {
                Version = BodyVersion,
                Remark = "",
                Entries = new List<VaultEntry>()
            };
        }

        public static bool TryUnlockBody(VaultBody body, string password, out byte[] key, out string error)
        {
            key = null;
            error = null;
            if (body == null || !body.HasPassword)
            {
                error = "尚未设置查看密码";
                return false;
            }
            byte[] salt = body.SaltBytes();
            if (salt == null)
            {
                error = "查看密码数据损坏";
                return false;
            }
            try
            {
                key = VaultCrypto.DeriveKey(password, salt);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            if (!VaultCrypto.CheckVerifier(key, body.Verifier))
            {
                key = null;
                error = "查看密码不正确";
                return false;
            }
            if (body.Entries != null)
            {
                for (int i = 0; i < body.Entries.Count; i++)
                {
                    string err;
                    if (!VaultText.TryReveal(body.Entries[i], key, out err))
                    {
                        error = err;
                        return false;
                    }
                }
            }
            return true;
        }

        public static void ApplyPassword(VaultBody body, byte[] salt, byte[] key)
        {
            if (body == null) return;
            body.Salt = Convert.ToBase64String(salt);
            body.Verifier = VaultCrypto.MakeVerifier(key);
        }

        private static void EnsureLoaded()
        {
            lock (_sync)
            {
                if (!_loaded) LoadLocked();
            }
        }

        private static void LoadLocked()
        {
            _items = new List<VaultItem>();
            _sampleSeeded = false;
            _loaded = true;
            Directory.CreateDirectory(DataPaths.VaultDir);
            string path = DataPaths.VaultIndexJson;
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var file = JsonConvert.DeserializeObject<VaultIndexFile>(json);
                    if (file != null)
                    {
                        _sampleSeeded = file.SampleSeeded;
                        if (file.Items != null)
                        {
                            for (int i = 0; i < file.Items.Count; i++)
                            {
                                var it = file.Items[i];
                                if (it == null) continue;
                                Normalize(it);
                                _items.Add(it);
                            }
                        }
                    }
                }
                catch { }
            }
            if (!_sampleSeeded)
            {
                SeedSamplesLocked();
                _sampleSeeded = true;
                try { WriteLocked(); }
                catch { }
            }
        }

        private const string SampleMultiAccountUid = "vaultexample00000000000000000001";
        private const string SampleMultiPlatformUid = "vaultexample00000000000000000002";

        private static void SeedSamplesLocked()
        {
            var now = DateTime.Now;
            if (FindLocked(SampleMultiAccountUid) == null)
            {
                var item = new VaultItem
                {
                    Uid = SampleMultiAccountUid,
                    Title = "【示例】一平台多账号（内网 GitLab）",
                    CreatedAt = now,
                    UpdatedAt = now,
                    EntryCount = 2
                };
                Normalize(item);
                var body = NewEmptyBody();
                body.Remark = "同一平台多名同事各用各的号：平台名填一样、账号不同。本篇可删。";
                body.Entries.Add(PlainEntry("GitLab", "alice", "AliceDemo1", "https://gitlab.example.com"));
                body.Entries.Add(PlainEntry("GitLab", "bob", "BobDemo2", "https://gitlab.example.com"));
                WriteBodyFileLocked(item.Uid, body);
                _items.Insert(0, item);
            }
            if (FindLocked(SampleMultiPlatformUid) == null)
            {
                var item = new VaultItem
                {
                    Uid = SampleMultiPlatformUid,
                    Title = "【示例】多平台一账号（协作套件）",
                    CreatedAt = now,
                    UpdatedAt = now,
                    EntryCount = 1
                };
                Normalize(item);
                var body = NewEmptyBody();
                body.Remark = "多个平台共用同一套账号密码，平台名用 / 写在一行。本篇可删。";
                var e = PlainEntry("GitLab", "ops", "SharedDemo1", "https://gitlab.example.com");
                e.Platforms.Add("Jenkins");
                e.Platforms.Add("禅道");
                body.Entries.Add(e);
                WriteBodyFileLocked(item.Uid, body);
                _items.Insert(0, item);
            }
        }

        private static VaultEntry PlainEntry(string platform, string account, string password, string url)
        {
            var e = new VaultEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Platforms = new List<string>(),
                Account = account,
                Password = password,
                Url = url
            };
            if (!string.IsNullOrEmpty(platform))
                e.Platforms.Add(platform);
            return e;
        }

        private static void Normalize(VaultItem it)
        {
            if (it == null) return;
            if (string.IsNullOrEmpty(it.Uid)) it.Uid = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(it.Title)) it.Title = VaultText.DefaultTitle;
            if (it.CreatedAt == default(DateTime)) it.CreatedAt = DateTime.Now;
            if (it.UpdatedAt == default(DateTime)) it.UpdatedAt = it.CreatedAt;
            if (it.EntryCount < 0) it.EntryCount = 0;
        }

        private static bool TitleExistsLocked(string title, string exceptUid)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (exceptUid != null && _items[i].Uid == exceptUid) continue;
                if (string.Equals(_items[i].Title, title, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void SchedulePersistLocked()
        {
            int ms = AppSettings.CurrentHistoryPersistDelayMs();
            if (_diskTimer != null) _diskTimer.Dispose();
            _diskTimer = new Timer(BackgroundPersist, null, ms, Timeout.Infinite);
        }

        private static void BackgroundPersist(object state)
        {
            if (Interlocked.Exchange(ref _inflight, 1) == 1)
            {
                lock (_sync)
                {
                    _diskTimer = new Timer(BackgroundPersist, null, 50, Timeout.Infinite);
                }
                return;
            }
            try
            {
                lock (_sync)
                {
                    WriteLocked();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _inflight, 0);
            }
        }

        private static void WriteLocked()
        {
            Directory.CreateDirectory(DataPaths.VaultDir);
            var file = new VaultIndexFile
            {
                Version = IndexVersion,
                SampleSeeded = _sampleSeeded,
                Items = _items
            };
            string json = JsonConvert.SerializeObject(file, Formatting.Indented);
            string path = DataPaths.VaultIndexJson;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);
            File.Copy(tmp, path, true);
            try { File.Delete(tmp); } catch { }
        }

        private static VaultBody ReadBodyFileLocked(string uid)
        {
            string path = DataPaths.VaultBodyPath(uid);
            if (!File.Exists(path))
                return NewEmptyBody();
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var body = JsonConvert.DeserializeObject<VaultBody>(json);
                if (body == null) return NewEmptyBody();
                if (body.Entries == null) body.Entries = new List<VaultEntry>();
                for (int i = 0; i < body.Entries.Count; i++)
                    VaultText.NormalizeEntry(body.Entries[i]);
                return body;
            }
            catch
            {
                return NewEmptyBody();
            }
        }

        private static void WriteBodyFileLocked(string uid, VaultBody body)
        {
            Directory.CreateDirectory(DataPaths.VaultItemDir(uid));
            if (body == null) body = NewEmptyBody();
            body.Version = BodyVersion;
            if (body.Entries == null) body.Entries = new List<VaultEntry>();
            string json = JsonConvert.SerializeObject(body, Formatting.Indented);
            string path = DataPaths.VaultBodyPath(uid);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);
            File.Copy(tmp, path, true);
            try { File.Delete(tmp); } catch { }
        }

        private static VaultItem FindLocked(string uid)
        {
            int i = IndexOfLocked(uid);
            return i < 0 ? null : _items[i];
        }

        private static int IndexOfLocked(string uid)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Uid == uid) return i;
            }
            return -1;
        }

        private static VaultItem Clone(VaultItem s)
        {
            if (s == null) return null;
            return new VaultItem
            {
                Uid = s.Uid,
                Title = s.Title,
                Pinned = s.Pinned,
                ReadOnly = s.ReadOnly,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
                EntryCount = s.EntryCount,
                HasSecret = s.HasSecret
            };
        }

        private static VaultBody CloneBody(VaultBody s)
        {
            if (s == null) return NewEmptyBody();
            var b = new VaultBody
            {
                Version = s.Version,
                Remark = s.Remark,
                Salt = s.Salt,
                Verifier = s.Verifier,
                Entries = new List<VaultEntry>()
            };
            if (s.Entries != null)
            {
                for (int i = 0; i < s.Entries.Count; i++)
                    b.Entries.Add(CloneEntry(s.Entries[i]));
            }
            return b;
        }

        private static VaultEntry CloneEntry(VaultEntry s)
        {
            if (s == null) return null;
            var e = new VaultEntry
            {
                Id = s.Id,
                Account = s.Account,
                Password = s.Password,
                Url = s.Url,
                Masked = s.Masked,
                AccountEnc = s.AccountEnc,
                PasswordEnc = s.PasswordEnc,
                Platforms = new List<string>()
            };
            if (s.Platforms != null)
            {
                for (int i = 0; i < s.Platforms.Count; i++)
                    e.Platforms.Add(s.Platforms[i]);
            }
            return e;
        }
    }
}
