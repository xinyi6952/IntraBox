using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IntraBox.Core
{
    /// <summary>
    /// 全局热键（Win32 RegisterHotKey）。参考 ShareX / Snapture：挂在主窗口 HWND 上，托盘隐藏时仍有效。
    /// 默认 Ctrl+Alt+A。
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        public const int HotkeyIdCapture = 0x4942; // 'IB'
        public const int HotkeyIdRuler = 0x4943;
        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint VkA = 0x41;
        private const uint VkM = 0x4D;

        private uint _mod = ModControl | ModAlt;
        private uint _vk = VkA;
        private uint _rulerMod = ModControl | ModShift;
        private uint _rulerVk = VkM;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public static HotkeyService Instance { get; private set; }

        public event EventHandler CaptureRequested;
        public event EventHandler RulerRequested;

        public bool Registered { get; private set; }

        private HwndSource _source;
        private IntPtr _hwnd;

        public static void Install(Window window)
        {
            if (Instance != null) return;
            Instance = new HotkeyService();
            Instance.Attach(window);
        }

        public static void Uninstall()
        {
            if (Instance == null) return;
            Instance.Dispose();
            Instance = null;
        }

        private void Attach(Window window)
        {
            if (window == null) return;
            var helper = new WindowInteropHelper(window);
            helper.EnsureHandle();
            _hwnd = helper.Handle;
            _source = HwndSource.FromHwnd(_hwnd);
            if (_source != null)
                _source.AddHook(WndProc);

            var s = ConfigManager.Instance.Settings;
            _mod = BuildModifiers(s.CaptureHotkeyCtrl, s.CaptureHotkeyAlt, s.CaptureHotkeyShift);
            _vk = s.CaptureHotkeyVk > 0 ? (uint)s.CaptureHotkeyVk : VkA;
            if (_mod == 0) _mod = ModControl | ModAlt;
            Registered = RegisterHotKey(_hwnd, HotkeyIdCapture, _mod, _vk);
            if (!Registered)
            {
                _mod = ModControl | ModAlt;
                _vk = VkA;
                Registered = RegisterHotKey(_hwnd, HotkeyIdCapture, _mod, _vk);
            }
            _rulerMod = BuildModifiers(s.RulerHotkeyCtrl, s.RulerHotkeyAlt, s.RulerHotkeyShift);
            _rulerVk = s.RulerHotkeyVk > 0 ? (uint)s.RulerHotkeyVk : VkM;
            if (_rulerMod == 0) _rulerMod = ModControl | ModShift;
            if (!RegisterHotKey(_hwnd, HotkeyIdRuler, _rulerMod, _rulerVk))
            {
                _rulerMod = ModControl | ModShift;
                _rulerVk = VkM;
                RegisterHotKey(_hwnd, HotkeyIdRuler, _rulerMod, _rulerVk);
            }
        }

        /// <summary>当前热键的可读文本，如 Ctrl+Alt+A。</summary>
        public string DisplayText
        {
            get { return Format(_mod, _vk); }
        }

        /// <summary>重新注册热键。失败时保留原绑定并返回 false。</summary>
        public bool Rebind(bool ctrl, bool alt, bool shift, int vk)
        {
            if (_hwnd == IntPtr.Zero) return false;
            if (vk <= 0) vk = (int)VkA;
            uint mod = BuildModifiers(ctrl, alt, shift);
            if (mod == 0) return false;
            try { UnregisterHotKey(_hwnd, HotkeyIdCapture); } catch { }
            bool ok = RegisterHotKey(_hwnd, HotkeyIdCapture, mod, (uint)vk);
            if (ok)
            {
                _mod = mod;
                _vk = (uint)vk;
                Registered = true;
                return true;
            }
            Registered = RegisterHotKey(_hwnd, HotkeyIdCapture, _mod, _vk);
            return false;
        }

        public static string Format(uint mod, uint vk)
        {
            string s = "";
            if ((mod & ModControl) != 0) s += "Ctrl+";
            if ((mod & ModAlt) != 0) s += "Alt+";
            if ((mod & ModShift) != 0) s += "Shift+";
            if (vk >= 0x41 && vk <= 0x5A) s += (char)vk;
            else if (vk >= 0x30 && vk <= 0x39) s += (char)vk;
            else s += "Vk" + vk;
            return s;
        }

        private static uint BuildModifiers(bool ctrl, bool alt, bool shift)
        {
            uint mod = 0;
            if (ctrl) mod |= ModControl;
            if (alt) mod |= ModAlt;
            if (shift) mod |= ModShift;
            return mod;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey)
            {
                int id = wParam.ToInt32();
                if (id == HotkeyIdCapture)
                {
                    var handler = CaptureRequested;
                    if (handler != null) handler(this, EventArgs.Empty);
                    handled = true;
                }
                else if (id == HotkeyIdRuler)
                {
                    var handler = RulerRequested;
                    if (handler != null) handler(this, EventArgs.Empty);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)
            {
                try { UnregisterHotKey(_hwnd, HotkeyIdCapture); } catch { }
                try { UnregisterHotKey(_hwnd, HotkeyIdRuler); } catch { }
            }
            if (_source != null)
            {
                try { _source.RemoveHook(WndProc); } catch { }
                _source = null;
            }
            Registered = false;
            _hwnd = IntPtr.Zero;
        }
    }
}
