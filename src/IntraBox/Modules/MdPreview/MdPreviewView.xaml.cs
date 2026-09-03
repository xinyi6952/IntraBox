using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IntraBox.Controls;
using IntraBox.Core;
using Markdig;

namespace IntraBox.Modules.MdPreview
{
    public partial class MdPreviewView : UserControl, IModuleView, ILeaveGuard
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        private void CancelPending()
        {
            _gate.Cancel();
        }

        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private bool _restoring;
        private bool _previewMax;
        private string _filePath;
        private EncodingChoice _fileEnc;
        private string _savedText = "";

        // 默认 Markdown.ToHtml 只开 CommonMark：表格/删除线/任务列表都不会解析，预览会像源码。
        private static readonly MarkdownPipeline MdPipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        public MdPreviewView()
        {
            InitializeComponent();
            _timer.Interval = TimeSpan.FromMilliseconds(350);
            _timer.Tick += (s, e) => { _timer.Stop(); Render(); };
            InputBox.TextChangedByUser += (s, e) =>
            {
                if (_restoring) return;
                _timer.Stop();
                _timer.Start();
            };
        }

        private void MaxBtn_Click(object sender, RoutedEventArgs e)
        {
            _previewMax = !_previewMax;
            InPlaceMaximize.Apply(_previewMax, PreviewHost, 0, 1, 2, 3, LeftPane, Splitter);
            if (IconExpand != null) IconExpand.Visibility = _previewMax ? Visibility.Collapsed : Visibility.Visible;
            if (IconRestore != null) IconRestore.Visibility = _previewMax ? Visibility.Visible : Visibility.Collapsed;
            MaxBtn.ToolTip = _previewMax ? "还原布局" : "最大化预览";
        }

        private async void OpenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsDirty() && !ConfirmHelper.Action("有未保存的修改，打开新文件将丢弃修改，确定打开？", "打开确认"))
                return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "打开 Markdown 文件",
                Filter = "Markdown|*.md;*.markdown|文本|*.txt|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            string path = dlg.FileName;
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;

            string text;
            EncodingChoice used = TextFileCodec.Auto;
            try
            {
                text = await Task.Run(() => TextFileCodec.ReadAll(path, TextFileCodec.Auto, SizeLimits.MaxFileBytes, out used), token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) ShowError(ex.RootMessage()); return; }
            if (version != _gate.Version) return;

            InputBox.Text = text;
            _filePath = path;
            _fileEnc = used;
            _savedText = text ?? "";
            Render();
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            Save();
        }

        private async void ReloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                MessageBox.Show("尚未打开文件。", "重载", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (IsDirty() && !ConfirmHelper.Action("有未保存的修改，重载将丢弃修改，确定重载？", "重载确认"))
                return;

            string path = _filePath;
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;

            string text;
            EncodingChoice used = TextFileCodec.Auto;
            try
            {
                text = await Task.Run(() => TextFileCodec.ReadAll(path, TextFileCodec.Auto, SizeLimits.MaxFileBytes, out used), token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) ShowError(ex.RootMessage()); return; }
            if (version != _gate.Version) return;

            InputBox.Text = text;
            _fileEnc = used;
            _savedText = text ?? "";
            Render();
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsDirty() && !ConfirmHelper.Action("有未保存的修改，清空将丢弃修改，确定清空？", "清空确认"))
                return;
            InputBox.Text = "";
            _filePath = null;
            _savedText = "";
            Render();
            HistoryManager.Save("mdpreview", new Dictionary<string, object> { { "input", "" }, { "file", "" } });
        }

        public void OnActivated()
        {
            Dictionary<string, object> st;
            if (HistoryManager.TryLoad("mdpreview", out st))
            {
                _restoring = true;
                InputBox.Text = HistoryManager.GetString(st, "input");
                var file = HistoryManager.GetString(st, "file");
                _filePath = string.IsNullOrEmpty(file) ? null : file;
                _restoring = false;
            }
            _savedText = InputBox.Text ?? "";
            Render();
        }

        public void OnDeactivated()
        {
            _timer.Stop();
            CancelPending();
            HistoryManager.Save("mdpreview", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" },
                { "file", _filePath ?? "" }
            });
        }

        public bool CanLeave()
        {
            if (!IsDirty()) return true;
            var r = ConfirmHelper.Unsaved("Markdown 有未保存的修改，是否保存？", "未保存确认");
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Save();
            return true;
        }

        private bool IsDirty()
        {
            return (InputBox.Text ?? "") != _savedText;
        }

        private bool Save()
        {
            string path = _filePath;
            if (string.IsNullOrEmpty(path))
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存 Markdown",
                    Filter = "Markdown|*.md|文本|*.txt|所有文件|*.*",
                    FileName = "untitled.md"
                };
                if (dlg.ShowDialog() != true) return false;
                path = dlg.FileName;
            }
            try
            {
                var enc = _fileEnc ?? TextFileCodec.Utf8;
                TextFileCodec.WriteAll(path, InputBox.Text ?? "", enc);
                _filePath = path;
                _savedText = InputBox.Text ?? "";
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.RootMessage(), "保存", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // Markdig 解析是毫秒级，放后台线程反而引入「取消+版本号」误丢弃结果的 Bug，故保持同步。
        private void Render()
        {
            try
            {
                string md = InputBox.Text ?? "";
                string html = Markdown.ToHtml(md, MdPipeline);
                Preview.NavigateToString(BuildPage(html));
                _pendingSearch = SearchBox != null ? SearchBox.Text : "";
                Preview.LoadCompleted -= Preview_LoadCompleted;
                Preview.LoadCompleted += Preview_LoadCompleted;
            }
            catch (Exception ex)
            {
                ShowError(ex.RootMessage());
            }
        }

        private string _pendingSearch = "";

        private void Preview_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            if (!string.IsNullOrEmpty(_pendingSearch))
                InvokeSearch(_pendingSearch, 0);
        }

        // 页内搜索：注入 JS 做大小写不敏感高亮 + 计数 + 跳转。无 mshtml 依赖，纯脚本桥。
        private static string BuildPage(string bodyHtml)
        {
            return "<!DOCTYPE html><html><head><meta charset='utf-8'><meta http-equiv='X-UA-Compatible' content='IE=edge'/><style>"
                + "body{font-family:'Segoe UI',sans-serif;padding:14px;line-height:1.6;background:#1e1e1e;color:#ddd;}"
                + "h1,h2,h3{line-height:1.25;margin:0.7em 0 0.4em;} h1{border-bottom:1px solid #444;padding-bottom:4px;}"
                + "code{background:#111;padding:2px 5px;border-radius:3px;font-family:Consolas,monospace;}"
                + "pre{background:#111;padding:10px;border-radius:6px;overflow:auto;} pre code{padding:0;background:transparent;}"
                + "a{color:#6cb6ff;} blockquote{border-left:3px solid #555;margin:0;padding:0 12px;color:#aaa;}"
                + "table{border-collapse:collapse;margin:0.8em 0;} th,td{border:1px solid #555;padding:6px 12px;}"
                + "th{background:#333;font-weight:600;} del,s{color:#999;}"
                + "ul,ol{padding-left:1.8em;} img{max-width:100%;} hr{border:0;border-top:1px solid #444;}"
                + ".mdfind{background:#7a6400;color:#fff;border-radius:2px;}"
                + ".mdfind.cur{background:#c8a400;color:#000;outline:1px solid #c8a400;}"
                + "</style>"
                + "<script type='text/javascript'>"
                // 兼容 WebBrowser 的 IE 文档模式：不用 ES6/TreeWalker/scrollIntoView({block})，
                // 改用递归遍历文本节点 + 老式 className/标签数组 + scrollIntoView()。
                + "var _hits=[];var _cur=-1;"
                + "function _esc(s){return s.replace(/([.*+?^${}()|\\[\\]\\\\])/g,'\\\\$1');}"
                + "function _textNodes(root,out){var kids=root.childNodes;for(var i=0;i<kids.length;i++){var k=kids[i];if(k.nodeType===3){var p=k.parentNode;if(p&&p.nodeName!=='SCRIPT'&&p.nodeName!=='STYLE')out.push(k);}else if(k.nodeType===1){_textNodes(k,out);}}}"
                + "function mdClear(){for(var i=_hits.length-1;i>=0;i--){var s=_hits[i];if(s&&s.parentNode){var p=s.parentNode;var t=s.firstChild;while(t){p.insertBefore(t,s);t=s.firstChild;}p.removeChild(s);}}_hits=[];_cur=-1;}"
                + "function mdFind(q){mdClear();if(!q)return '0';var re=new RegExp(_esc(q),'gi');var nodes=[];_textNodes(document.body,nodes);"
                + "for(var i=0;i<nodes.length;i++){var t=nodes[i];var txt=t.nodeValue;re.lastIndex=0;var frag=null;var last=0;var had=false;var m;"
                + "while((m=re.exec(txt))!=null){if(!had){frag=document.createDocumentFragment();had=true;}"
                + "if(m.index>last)frag.appendChild(document.createTextNode(txt.substring(last,m.index)));"
                + "var mk=document.createElement('span');mk.className='mdfind';mk.appendChild(document.createTextNode(m[0]));frag.appendChild(mk);_hits.push(mk);"
                + "last=m.index+m[0].length;if(m[0].length===0)re.lastIndex++;}"
                + "if(had){if(last<txt.length)frag.appendChild(document.createTextNode(txt.substring(last)));t.parentNode.replaceChild(frag,t);}}"
                + "if(_hits.length===0)return '0';_cur=0;_scroll();return ''+_hits.length;}"
                + "function mdNext(dir){if(_hits.length===0)return '0';_cur+=dir;if(_cur>=_hits.length)_cur=0;if(_cur<0)_cur=_hits.length-1;_scroll();return ''+(_cur+1);}"
                + "function _scroll(){for(var i=0;i<_hits.length;i++){_hits[i].className=(i===_cur)?'mdfind cur':'mdfind';}if(_cur>=0&&_hits[_cur]){_hits[_cur].scrollIntoView(false);}}"
                + "</script></head><body>" + bodyHtml + "</body></html>";
        }

        private void ShowError(string message)
        {
            string msg = (message ?? "").Replace("<", "&lt;").Replace(">", "&gt;");
            Preview.NavigateToString("<pre>" + msg + "</pre>");
        }

        // ---------- 预览内搜索 ----------

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            InvokeSearch(SearchBox.Text, 0);
        }

        private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                int dir = System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift ? -1 : 1;
                InvokeSearch(SearchBox.Text, dir);
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                SearchBox.Text = "";
            }
        }

        // dir: 0=重新计数并跳到第一个, 1=下一个, -1=上一个
        private void InvokeSearch(string query, int dir)
        {
            if (SearchCount == null) return;
            if (string.IsNullOrEmpty(query))
            {
                SearchCount.Text = "";
                _lastTotal = 0;
                TryInvoke("mdClear");
                return;
            }
            if (dir == 0)
            {
                int total = ToInt(TryInvoke("mdFind", new object[] { query }));
                _lastTotal = total;
                SearchCount.Text = total > 0 ? "1/" + total : "无结果";
            }
            else
            {
                int cur = ToInt(TryInvoke("mdNext", new object[] { dir }));
                SearchCount.Text = _lastTotal > 0 ? cur + "/" + _lastTotal : "";
            }
        }

        private int _lastTotal;

        private static int ToInt(object o)
        {
            int v;
            return (o != null && int.TryParse(o.ToString(), out v)) ? v : 0;
        }

        private object TryInvoke(string func, object[] args = null)
        {
            try
            {
                if (Preview.Document == null) return null;
                return Preview.InvokeScript(func, args);
            }
            catch { return null; }
        }

    }
}
