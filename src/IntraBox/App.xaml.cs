using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using IntraBox.Core;
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
                // 已有实例在运行：通知它显示窗口，然后退出本实例
                try
                {
                    using (var ev = EventWaitHandle.OpenExisting(ShowSignalName))
                        ev.Set();
                }
                catch { /* 已有实例可能尚未就绪，忽略 */ }
                Shutdown();
                return;
            }

            base.OnStartup(e);

            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);

            // 全局异常兜底
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // 加载配置、初始化托盘、创建主窗口（仍用自研托盘，不用 hc:NotifyIcon）
            ConfigManager.Instance.Load();
            HistoryManager.LoadFromDisk();
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

        /// <summary>将异常链（含内部异常与堆栈）写入程序目录 error.log，便于定位。</summary>
        private static void LogException(Exception ex)
        {
            try
            {
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
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

        /// <summary>error.log 超过 1MB 时只保留尾部 256KB，避免托盘常驻把程序目录写满。</summary>
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
    }
}
