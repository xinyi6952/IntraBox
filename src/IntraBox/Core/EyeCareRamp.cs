using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace IntraBox.Core
{
    /// <summary>
    /// 对活动显示器写伽马表；驱动拒绝或读回仍是原色时，Win8+ 改用系统全屏校色。
    /// 失败不抛，由调用方提示。
    /// </summary>
    public static class EyeCareRamp
    {
        private const int DisplayDeviceActive = 0x00000001;
        private const int RampLen = 768;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, IntPtr ramp);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDeviceGammaRamp(IntPtr hdc, IntPtr ramp);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("Magnification.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MagInitialize();

        [DllImport("Magnification.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MagUninitialize();

        [DllImport("Magnification.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MagSetFullscreenColorEffect(IntPtr pEffect);

        private static readonly object Sync = new object();
        private static Dictionary<string, ushort[]> _backup;
        private static bool _magInited;
        private static bool _magActive;
        private static bool? _magAvailable;

        public static bool HasBackup
        {
            get { lock (Sync) return _backup != null && _backup.Count > 0; }
        }

        public static bool TryApply(int kelvin, int brightness, out string error)
        {
            error = null;
            ushort[] red = new ushort[256];
            ushort[] green = new ushort[256];
            ushort[] blue = new ushort[256];
            EyeCareHelper.BuildRamp(kelvin, brightness, red, green, blue);
            ushort[] want = PackRamp(red, green, blue);
            lock (Sync)
            {
                List<string> keys = ListActiveDisplays();
                int ok = 0;
                for (int i = 0; i < keys.Count; i++)
                {
                    if (ApplyGdi(keys[i], want, true)) ok++;
                }
                if (ok == 0 && ApplyGdi(null, want, true)) ok++;
                if (ok > 0)
                {
                    if (_magActive) SetMagIdentity();
                    return true;
                }

                if (TryApplyMag(kelvin, brightness))
                    return true;

                error = "当前显示器未接受色温（部分笔记本驱动会忽略软件伽马）。";
                return false;
            }
        }

        /// <summary>写回开启前的曲线。clearBackup 为 true 时丢掉备份（用户关闭）。</summary>
        public static void Restore(bool clearBackup)
        {
            lock (Sync)
            {
                if (_backup != null)
                {
                    foreach (var kv in _backup)
                        ApplyGdi(string.IsNullOrEmpty(kv.Key) ? null : kv.Key, kv.Value, false);
                }
                if (_magActive || _magInited)
                    SetMagIdentity();
                if (clearBackup)
                {
                    _backup = null;
                    ShutdownMag();
                }
            }
        }

        private static bool ApplyGdi(string device, ushort[] ramp, bool captureBackup)
        {
            IntPtr hdc = IntPtr.Zero;
            bool created = false;
            try
            {
                if (!string.IsNullOrEmpty(device))
                {
                    hdc = CreateDC("DISPLAY", device, null, IntPtr.Zero);
                    created = hdc != IntPtr.Zero;
                }
                if (hdc == IntPtr.Zero)
                    hdc = GetDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero) return false;

                string key = device ?? "";
                if (captureBackup)
                    CaptureBackup(hdc, key);

                if (!SetPacked(hdc, ramp)) return false;
                if (!LooksApplied(hdc, ramp)) return false;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    if (created) DeleteDC(hdc);
                    else ReleaseDC(IntPtr.Zero, hdc);
                }
            }
        }

        private static void CaptureBackup(IntPtr hdc, string key)
        {
            if (_backup == null) _backup = new Dictionary<string, ushort[]>(StringComparer.OrdinalIgnoreCase);
            if (_backup.ContainsKey(key)) return;
            ushort[] orig = new ushort[RampLen];
            if (!GetPacked(hdc, orig)) orig = IdentityPacked();
            _backup[key] = orig;
        }

        private static bool LooksApplied(IntPtr hdc, ushort[] want)
        {
            if (IsNearIdentity(want)) return true;
            ushort[] got = new ushort[RampLen];
            if (!GetPacked(hdc, got)) return true;
            if (IsNearIdentity(got)) return false;
            return true;
        }

        private static bool TryApplyMag(int kelvin, int brightness)
        {
            if (!EnsureMag()) return false;
            float[] matrix = new float[25];
            EyeCareHelper.BuildColorMatrix(kelvin, brightness, matrix);
            if (!SetMagMatrix(matrix)) return false;
            if (!_magActive)
                AppLog.Info("护眼：显示器拒绝伽马表，已改用系统全屏校色");
            _magActive = true;
            return true;
        }

        private static bool SetMagIdentity()
        {
            if (!_magInited) return false;
            float[] matrix = new float[25];
            EyeCareHelper.FillIdentityColorMatrix(matrix);
            bool ok = SetMagMatrix(matrix);
            _magActive = false;
            return ok;
        }

        private static bool EnsureMag()
        {
            if (_magInited) return true;
            if (_magAvailable == false) return false;
            if (_magAvailable == null)
                _magAvailable = HasMagFullscreen();
            if (_magAvailable != true) return false;
            try
            {
                _magInited = MagInitialize();
            }
            catch (DllNotFoundException)
            {
                _magAvailable = false;
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                _magAvailable = false;
                return false;
            }
            catch
            {
                return false;
            }
            return _magInited;
        }

        private static void ShutdownMag()
        {
            if (!_magInited) return;
            try { MagUninitialize(); }
            catch { }
            _magInited = false;
            _magActive = false;
        }

        private static bool HasMagFullscreen()
        {
            try
            {
                IntPtr h = LoadLibrary("Magnification.dll");
                if (h == IntPtr.Zero) return false;
                return GetProcAddress(h, "MagSetFullscreenColorEffect") != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetMagMatrix(float[] matrix)
        {
            if (matrix == null || matrix.Length < 25) return false;
            GCHandle pin = GCHandle.Alloc(matrix, GCHandleType.Pinned);
            try
            {
                return MagSetFullscreenColorEffect(pin.AddrOfPinnedObject());
            }
            catch (EntryPointNotFoundException)
            {
                _magAvailable = false;
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                pin.Free();
            }
        }

        private static bool SetPacked(IntPtr hdc, ushort[] packed)
        {
            return WithPinned(packed, ptr => SetDeviceGammaRamp(hdc, ptr));
        }

        private static bool GetPacked(IntPtr hdc, ushort[] packed)
        {
            return WithPinned(packed, ptr => GetDeviceGammaRamp(hdc, ptr));
        }

        private static bool WithPinned(ushort[] packed, Func<IntPtr, bool> act)
        {
            if (packed == null || packed.Length < RampLen || act == null) return false;
            GCHandle pin = GCHandle.Alloc(packed, GCHandleType.Pinned);
            try
            {
                return act(pin.AddrOfPinnedObject());
            }
            finally
            {
                pin.Free();
            }
        }

        private static ushort[] PackRamp(ushort[] red, ushort[] green, ushort[] blue)
        {
            var packed = new ushort[RampLen];
            Array.Copy(red, 0, packed, 0, 256);
            Array.Copy(green, 0, packed, 256, 256);
            Array.Copy(blue, 0, packed, 512, 256);
            return packed;
        }

        private static ushort[] IdentityPacked()
        {
            var packed = new ushort[RampLen];
            for (int i = 0; i < 256; i++)
            {
                ushort v = (ushort)(i * 256);
                packed[i] = v;
                packed[256 + i] = v;
                packed[512 + i] = v;
            }
            return packed;
        }

        private static bool IsNearIdentity(ushort[] packed)
        {
            if (packed == null || packed.Length < RampLen) return false;
            int err = 0;
            for (int i = 0; i < 256; i++)
            {
                int expect = i * 256;
                err += Math.Abs(packed[i] - expect);
                err += Math.Abs(packed[256 + i] - expect);
                err += Math.Abs(packed[512 + i] - expect);
                if (err > 24000) return false;
            }
            return true;
        }

        private static List<string> ListActiveDisplays()
        {
            var list = new List<string>();
            for (uint i = 0; i < 16; i++)
            {
                var d = new DisplayDevice();
                d.cb = Marshal.SizeOf(typeof(DisplayDevice));
                if (!EnumDisplayDevices(null, i, ref d, 0)) break;
                if ((d.StateFlags & DisplayDeviceActive) == 0) continue;
                if (string.IsNullOrEmpty(d.DeviceName)) continue;
                list.Add(d.DeviceName);
            }
            return list;
        }
    }
}
