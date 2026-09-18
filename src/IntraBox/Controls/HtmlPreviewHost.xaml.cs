using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using IntraBox.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IntraBox.Controls
{
    /// <summary>
    /// 本地 HTML 预览：Win10/11 用程序目录 WebView2Runtime（Edge），Win7 用 IE。
    /// 随包内核缺失或启动失败时才退 IE / 纯文本（故障，不是常规降级）。
    /// </summary>
    public partial class HtmlPreviewHost : UserControl
    {
        public const string VirtualFilesHost = "intrabox.files";

        public const string EngineWebView2Fixed = "webview2-fixed";
        public const string EngineIe = "ie";
        public const string EngineText = "text";

        private WebView2 _wv;
        private bool _wvReady;
        private bool _ieReady;
        private string _pendingHtml;
        private string _pendingFilesDir;
        private string _mappedDir;
        private string _engine = EngineText;
        private bool _initStarted;

        public event EventHandler EngineChanged;
        public event EventHandler DocumentReady;

        public string Engine
        {
            get { return _engine; }
        }

        public string StatusText { get; private set; }

        public HtmlPreviewHost()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            StartInit();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Release();
            DisposeWebView();
        }

        public void SetHtml(string html)
        {
            SetHtml(html, null);
        }

        public void SetHtml(string html, string localFilesDir)
        {
            _pendingHtml = html ?? "";
            _pendingFilesDir = localFilesDir;
            if (_wvReady) ShowWebView2(_pendingHtml, _pendingFilesDir);
            else if (_ieReady) ShowIe(_pendingHtml);
            else if (_engine == EngineText && _initStarted && !WinCompatHelper.IsWindows10OrNewer())
                ShowText(_pendingHtml);
        }

        public void Release()
        {
            _pendingHtml = null;
            if (_wv != null && _wvReady)
            {
                try { _wv.CoreWebView2.Navigate("about:blank"); }
                catch { }
            }
            if (IePreview != null)
            {
                IePreview.LoadCompleted -= Ie_LoadCompleted;
                try { IePreview.Navigate(new Uri("about:blank")); }
                catch { }
            }
            if (TextFallback != null) TextFallback.Text = "";
        }

        private void DisposeWebView()
        {
            if (_wv == null) return;
            try { _wv.Dispose(); }
            catch { }
            if (_wv.Parent != null)
            {
                try { Surface.Children.Remove(_wv); }
                catch { }
            }
            _wv = null;
            _wvReady = false;
            _initStarted = false;
            _mappedDir = null;
        }

        public async Task<object> TryInvokeAsync(string func, object[] args)
        {
            if (string.IsNullOrEmpty(func)) return null;
            if (_wvReady && _wv != null && _wv.CoreWebView2 != null)
            {
                try
                {
                    string json = await _wv.CoreWebView2.ExecuteScriptAsync(BuildCallScript(func, args));
                    return UnwrapScriptJson(json);
                }
                catch (Exception ex)
                {
                    CompatDegrade.Once("wv2-script", "WebView2 预览查找脚本失败。", ex);
                    return null;
                }
            }
            if (_ieReady && IePreview != null)
            {
                try
                {
                    if (IePreview.Document == null) return null;
                    return IePreview.InvokeScript(func, args);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        private async void StartInit()
        {
            if (_initStarted) return;
            _initStarted = true;
            if (!WinCompatHelper.IsWindows10OrNewer())
            {
                UseIe(null, null, false);
                return;
            }

            string fixedDir = WinCompatHelper.FindWebView2FixedFolder();
            if (string.IsNullOrEmpty(fixedDir))
            {
                UseIe("未找到程序目录 WebView2Runtime，预览改用 IE。请运行 tools\\fetch-webview2-runtime.ps1 后重新打包。", null, true);
                return;
            }

            try
            {
                _wv = new WebView2();
                _wv.Visibility = Visibility.Collapsed;
                _wv.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 30, 30, 30);
                Surface.Children.Add(_wv);
            }
            catch (Exception ex)
            {
                UseIe("无法创建 WebView2 控件，预览改用 IE。", ex, true);
                return;
            }

            try
            {
                string userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IntraBox", "WebView2");
                await EnsureEnv(fixedDir, userData);
                FinishWebView2(EngineWebView2Fixed, null);
            }
            catch (Exception ex)
            {
                UseIe("程序目录 WebView2Runtime 无法启动，预览改用 IE。", ex, true);
            }
        }

        private async Task EnsureEnv(string browserFolder, string userData)
        {
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(browserFolder, userData);
            await _wv.EnsureCoreWebView2Async(env);
        }

        private void FinishWebView2(string engine, string hint)
        {
            _wvReady = true;
            _engine = engine;
            try
            {
                var s = _wv.CoreWebView2.Settings;
                s.AreHostObjectsAllowed = false;
                s.IsWebMessageEnabled = false;
                s.AreDefaultContextMenusEnabled = true;
                s.IsStatusBarEnabled = false;
                _wv.CoreWebView2.NavigationStarting -= Wv_NavigationStarting;
                _wv.CoreWebView2.NavigationStarting += Wv_NavigationStarting;
                _wv.CoreWebView2.NewWindowRequested -= Wv_NewWindowRequested;
                _wv.CoreWebView2.NewWindowRequested += Wv_NewWindowRequested;
                _wv.NavigationCompleted -= Wv_NavigationCompleted;
                _wv.NavigationCompleted += Wv_NavigationCompleted;
            }
            catch (Exception ex)
            {
                CompatDegrade.Once("wv2-set", "WebView2 设置失败，仍尝试显示预览。", ex);
            }
            _wv.Visibility = Visibility.Visible;
            if (IePreview != null) IePreview.Visibility = Visibility.Collapsed;
            if (TextFallback != null) TextFallback.Visibility = Visibility.Collapsed;
            SetHint(hint, false);
            StatusText = "预览（Edge）";
            RaiseEngine();
            if (_pendingHtml != null) ShowWebView2(_pendingHtml, _pendingFilesDir);
        }

        private void UseIe(string hint, Exception ex, bool errorLog)
        {
            if (errorLog)
                CompatDegrade.Once("preview-ie", string.IsNullOrEmpty(hint) ? "预览改用 IE。" : hint, ex);
            _ieReady = true;
            _engine = EngineIe;
            if (_wv != null) _wv.Visibility = Visibility.Collapsed;
            if (IePreview != null)
            {
                IePreview.Visibility = Visibility.Visible;
                IePreview.Navigating -= Ie_Navigating;
                IePreview.Navigating += Ie_Navigating;
                IePreview.LoadCompleted -= Ie_LoadCompleted;
                IePreview.LoadCompleted += Ie_LoadCompleted;
            }
            if (TextFallback != null) TextFallback.Visibility = Visibility.Collapsed;
            SetHint(hint, errorLog);
            StatusText = "预览（IE）";
            RaiseEngine();
            if (_pendingHtml != null) ShowIe(_pendingHtml);
        }

        private void ShowWebView2(string html, string filesDir)
        {
            try
            {
                MapFiles(filesDir);
                string page = HtmlPreviewNavHelper.WithInPageNavScript(RewriteFilesHost(html, filesDir));
                _wv.NavigateToString(page ?? "");
            }
            catch (Exception ex)
            {
                _wvReady = false;
                CompatDegrade.ErrorAlways("WebView2 加载 HTML 失败，改用 IE。", ex);
                UseIe("WebView2 显示失败，预览改用 IE。", ex, true);
            }
        }

        private void ShowIe(string html)
        {
            try
            {
                IePreview.NavigateToString(HtmlPreviewNavHelper.WithInPageNavScript(html ?? ""));
            }
            catch (Exception ex)
            {
                CompatDegrade.ErrorAlways("IE 预览失败，改用纯文本。", ex);
                ShowText(html);
            }
        }

        private void ShowText(string html)
        {
            _engine = EngineText;
            _ieReady = false;
            if (_wv != null) _wv.Visibility = Visibility.Collapsed;
            if (IePreview != null) IePreview.Visibility = Visibility.Collapsed;
            if (TextFallback != null)
            {
                TextFallback.Visibility = Visibility.Visible;
                TextFallback.Text = StripTags(html);
            }
            SetHint("预览引擎均不可用，已显示纯文本。编辑与保存不受影响。", true);
            StatusText = "预览（纯文本）";
            RaiseEngine();
        }

        private void MapFiles(string filesDir)
        {
            if (_wv == null || _wv.CoreWebView2 == null) return;
            if (string.IsNullOrEmpty(filesDir) || !Directory.Exists(filesDir)) return;
            string full = Path.GetFullPath(filesDir);
            if (string.Equals(_mappedDir, full, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                _wv.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualFilesHost, full, CoreWebView2HostResourceAccessKind.Allow);
                _mappedDir = full;
            }
            catch (Exception ex)
            {
                CompatDegrade.Once("wv2-map", "WebView2 映射笔记附件目录失败，插图可能不显示。", ex);
            }
        }

        private static string RewriteFilesHost(string html, string filesDir)
        {
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(filesDir)) return html;
            try
            {
                string fileUri = new Uri(Path.GetFullPath(filesDir)).AbsoluteUri.TrimEnd('/');
                return html.Replace(fileUri, "https://" + VirtualFilesHost);
            }
            catch
            {
                return html;
            }
        }

        private void Wv_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            string u = e.Uri ?? "";
            string id;
            if (HtmlPreviewNavHelper.TryGetInPageFragment(u, out id))
            {
                e.Cancel = true;
                ScrollWebView2(id);
                return;
            }
            if (HtmlPreviewNavHelper.IsVirtualFilesUri(u, VirtualFilesHost)) return;
            if (HtmlPreviewNavHelper.ShouldCancelUserBlank(u, e.IsUserInitiated))
            {
                e.Cancel = true;
                return;
            }
            if (HtmlPreviewNavHelper.IsEngineLoadUri(u)) return;
            e.Cancel = true;
            if (HtmlPreviewNavHelper.ShouldBlockExternal(u))
                CompatDegrade.Once("wv2-nav", "预览已拦截外网导航：" + u, null);
        }

        private void Wv_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            string u = e.Uri ?? "";
            string id;
            if (HtmlPreviewNavHelper.TryGetInPageFragment(u, out id))
                ScrollWebView2(id);
        }

        private async void ScrollWebView2(string id)
        {
            if (string.IsNullOrEmpty(id) || _wv == null || _wv.CoreWebView2 == null) return;
            string js = "ibxScroll(" + Newtonsoft.Json.JsonConvert.SerializeObject(id) + ")";
            try { await _wv.CoreWebView2.ExecuteScriptAsync(js); }
            catch { }
        }

        private void Ie_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            if (e == null) return;
            string u = e.Uri == null ? "" : e.Uri.ToString();
            string id;
            if (HtmlPreviewNavHelper.TryGetInPageFragment(u, out id))
            {
                e.Cancel = true;
                ScrollIe(id);
                return;
            }
            if (HtmlPreviewNavHelper.IsEngineLoadUri(u)) return;
            e.Cancel = true;
        }

        private void ScrollIe(string id)
        {
            if (string.IsNullOrEmpty(id) || IePreview == null) return;
            try { IePreview.InvokeScript("ibxScroll", new object[] { id }); }
            catch { }
        }

        private void Wv_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            EventHandler h = DocumentReady;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void Ie_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            if (e != null && e.Uri != null
                && string.Equals(e.Uri.ToString(), "about:blank", StringComparison.OrdinalIgnoreCase))
                return;
            EventHandler h = DocumentReady;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void SetHint(string text, bool error)
        {
            if (HintText == null) return;
            if (string.IsNullOrEmpty(text))
            {
                HintText.Visibility = Visibility.Collapsed;
                HintText.Text = "";
                return;
            }
            HintText.Visibility = Visibility.Visible;
            FormHint.Apply(HintText, text, error);
        }

        private void RaiseEngine()
        {
            EventHandler h = EngineChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private static string BuildCallScript(string func, object[] args)
        {
            var sb = new StringBuilder();
            sb.Append(func);
            sb.Append('(');
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    object a = args[i];
                    if (a == null) sb.Append("null");
                    else if (a is bool) sb.Append((bool)a ? "true" : "false");
                    else if (a is byte || a is sbyte || a is short || a is ushort || a is int || a is uint || a is long || a is ulong || a is float || a is double || a is decimal)
                        sb.Append(Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture));
                    else
                        sb.Append(Newtonsoft.Json.JsonConvert.SerializeObject(a.ToString()));
                }
            }
            sb.Append(')');
            return sb.ToString();
        }

        private static string UnwrapScriptJson(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "null") return null;
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<string>(json);
            }
            catch
            {
                return json.Trim('"');
            }
        }

        private static string StripTags(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            var sb = new StringBuilder(html.Length);
            bool tag = false;
            for (int i = 0; i < html.Length; i++)
            {
                char c = html[i];
                if (c == '<') { tag = true; continue; }
                if (c == '>') { tag = false; continue; }
                if (!tag) sb.Append(c);
            }
            return System.Net.WebUtility.HtmlDecode(sb.ToString());
        }
    }
}
