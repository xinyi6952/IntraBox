using System;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using IntraBox.Core;
using Microsoft.Win32;
using IntraBox.Modules.ClipboardHistory;
using IntraBox.Modules.Screenshot;
using IntraBox.Modules.ScreenRuler;

namespace IntraBox
{
    /// <summary>
    /// 应用入口：单实例（Mutex）、初始化配置/托盘、创建主窗口、全局异常兜底。
    /// 再次启动时通知已有实例显示主窗口，本实例直接退出，避免同时开两个进程。
    /// </summary>
    public partial class App : Application
    {
        private const string MutexName = "IntraBox_SingleInstance_Mutex";
        private const string ShowSignalName = "IntraBox_ShowWindow_Event";

        private Mutex _mutex;
        private bool _ownsMutex;
        private EventWaitHandle _showSignal;
        private TrayIconManager _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例检查：必须在 base.OnStartup 之前，避免二次实例做多余初始化
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                // 已有实例在运行。管理员二次启动不会提升旧进程权限，不能只唤醒托盘里的非管理员实例。
                if (ProcessIsAdmin())
                {
                    MessageBox.Show(
                        "IntraBox 已经在托盘运行。再次以管理员打开，并不会让已有进程获得管理员权限，Hosts 仍然可能保存失败。\n\n请先右键托盘图标选择退出，再以管理员身份打开 IntraBox。",
                        "IntraBox",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(ShowSignalName))
                            ev.Set();
                    }
                    catch { /* 已有实例可能尚未就绪，忽略 */ }
                }
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // WPF WebBrowser 默认 IE7 文档模式，表格/CSS 几乎不生效。写本进程的 IE11 仿真。
            EnableIe11ForWebBrowser();

            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);

            // 全局异常兜底
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // 加载配置、初始化托盘、创建主窗口（仍用自研托盘，不用 hc:NotifyIcon）
            DataPaths.Initialize();
            ConfigManager.Instance.Load();
            HistoryManager.LoadFromDisk();
            // 一次性迁移旧 key（generator → idgenerator），保证老用户显隐/上次工具/历史不丢
            ConfigManager.Instance.MigrateGeneratorKey();
            ConfigManager.Instance.EnsureNewToolsVisible();
            HistoryManager.MigrateGeneratorKey();
            ThemeManager.Apply(ConfigManager.Instance.Settings.Theme);
            WindowCaption.Hook();
            _tray = new TrayIconManager();
            _tray.Initialize();

            var window = new MainWindow();
            MainWindow = window;
            _tray.ConfirmExit = () => window.PrepareExit();
            WindowRestore.ShowAndRestore(window);
            HotkeyService.Install(window);
            if (HotkeyService.Instance != null)
            {
                HotkeyService.Instance.CaptureRequested += (s, ev) => CaptureOverlayWindow.ShowNew();
                HotkeyService.Instance.RulerRequested += (s, ev) => ScreenRulerOverlayWindow.ShowNew();
            }
            TryShowWelcome(window);
            window.RestoreLastModule();
            ClipboardMonitor.Start();
            IntraBox.Modules.Todo.TodoStore.Reload();
            IntraBox.Modules.Notes.NoteStore.Reload();
            IntraBox.Modules.Vault.VaultStore.Reload();
            IntraBox.Modules.Todo.TodoReminderService.Start();

            StartShowWindowListener();
        }

        private static void TryShowWelcome(Window window)
        {
            var s = ConfigManager.Instance.Settings;
            if (!s.FirstRun) return;
            var w = new WelcomeWindow();
            w.Owner = window;
            w.ShowDialog();
            s.FirstRun = false;
            ConfigManager.Instance.Save();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 仅首个实例拥有完整状态，才做保存与释放；二次实例只释放自身句柄
            if (_ownsMutex)
            {
                ClipboardMonitor.Stop();
                KeepAwakeService.SetEnabled(false);
                HistoryManager.Flush();
                IntraBox.Modules.FileOrganize.FileOrganizeStore.Flush();
                IntraBox.Modules.Todo.TodoStore.Flush();
                IntraBox.Modules.Notes.NoteStore.Flush();
                IntraBox.Modules.Vault.VaultStore.Flush();
                IntraBox.Modules.Todo.TodoReminderService.Stop();
                ConfigManager.Instance.Save();
                HotkeyService.Uninstall();
                if (_tray != null) { _tray.Dispose(); _tray = null; }
                if (_showSignal != null) { _showSignal.Dispose(); _showSignal = null; }
            }

            if (_mutex != null)
            {
                if (_ownsMutex) _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }

            base.OnExit(e);
        }

        /// <summary>显示并激活主窗口（二次启动通知已有实例）。</summary>
        private void ShowMainWindow()
        {
            WindowRestore.ShowAndRestore(MainWindow);
        }

        /// <summary>后台线程等待「显示窗口」信号，收到后切回 UI 线程。</summary>
        private void StartShowWindowListener()
        {
            var t = new Thread(() =>
            {
                while (_showSignal != null)
                {
                    try
                    {
                        if (_showSignal.WaitOne())
                            Dispatcher.Invoke((Action)ShowMainWindow);
                    }
                    catch { break; }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private static bool ProcessIsAdmin()
        {
            try
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException(e.Exception);
            var msg = e.Exception.Message;
            if (e.Exception.InnerException != null)
                msg += "\n\n内部错误：" + e.Exception.InnerException.Message;
            MessageBox.Show(
                "发生未处理的异常：\n" + msg,
                "IntraBox",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogException(e.ExceptionObject as Exception);
        }

        /// <summary>将异常链（含内部异常与堆栈）写入数据根 error.log，便于定位。</summary>
        private static void LogException(Exception ex)
        {
            try
            {
                DataPaths.Initialize();
                var path = DataPaths.ErrorLog;
                RotateLogIfNeeded(path);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
                var e = ex;
                while (e != null)
                {
                    sb.AppendLine("[" + e.GetType().FullName + "] " + e.Message);
                    sb.AppendLine(e.StackTrace);
                    sb.AppendLine("---");
                    e = e.InnerException;
                }
                System.IO.File.AppendAllText(path, sb.ToString());
            }
            catch { /* 日志写入失败不影响运行 */ }
        }

        /// <summary>error.log 超过 1MB 时只保留尾部 256KB，避免托盘常驻把数据目录写满。</summary>
        private static void RotateLogIfNeeded(string path)
        {
            const long maxBytes = 1024L * 1024L;
            const int keepBytes = 256 * 1024;
            if (!System.IO.File.Exists(path)) return;
            var info = new System.IO.FileInfo(path);
            if (info.Length <= maxBytes) return;

            byte[] tail;
            using (var fs = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
            {
                int take = (int)Math.Min(keepBytes, fs.Length);
                fs.Seek(-take, System.IO.SeekOrigin.End);
                tail = new byte[take];
                int read = 0;
                while (read < take)
                {
                    int n = fs.Read(tail, read, take - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < take)
                {
                    var smaller = new byte[read];
                    Buffer.BlockCopy(tail, 0, smaller, 0, read);
                    tail = smaller;
                }
            }
            int start = 0;
            for (int i = 0; i < tail.Length; i++)
            {
                if (tail[i] == (byte)'\n') { start = i + 1; break; }
            }
            using (var fs = System.IO.File.Create(path))
            {
                var header = System.Text.Encoding.UTF8.GetBytes("==== log rotated ====\r\n");
                fs.Write(header, 0, header.Length);
                if (start < tail.Length)
                    fs.Write(tail, start, tail.Length - start);
            }
        }

        /// <summary>
        /// WPF WebBrowser 默认按 IE7 渲染，表格边框和较新 CSS 几乎无效。
        /// 给本进程 exe 写入当前用户的 FEATURE_BROWSER_EMULATION = IE11。
        /// 必须在创建任何 WebBrowser 之前调用。
        /// </summary>
        private static void EnableIe11ForWebBrowser()
        {
            try
            {
                string exe = AppDomain.CurrentDomain.FriendlyName;
                if (string.IsNullOrEmpty(exe)) return;
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                {
                    if (key == null) return;
                    key.SetValue(exe, 11001, RegistryValueKind.DWord);
                }
            }
            catch { /* 写注册表失败时仍可预览，只是表格样式可能较差 */ }
        }
    }
}
