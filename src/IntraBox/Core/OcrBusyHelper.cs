using System;
using System.Threading;

namespace IntraBox.Core
{
    /// <summary>进程内 OCR 互斥：同时只允许一次识别（TesseractEngine 非线程安全）。</summary>
    public static class OcrBusyHelper
    {
        private static int _busy;

        public static event EventHandler BusyChanged;

        public static bool IsBusy
        {
            get { return Volatile.Read(ref _busy) != 0; }
        }

        /// <summary>抢到则 true 并通知 BusyChanged；已在识别则 false。</summary>
        public static bool TryBegin()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
            Raise();
            return true;
        }

        /// <summary>结束本次识别并通知。未占用时调用无效果。</summary>
        public static void End()
        {
            if (Interlocked.Exchange(ref _busy, 0) == 0) return;
            Raise();
        }

        private static void Raise()
        {
            EventHandler h = BusyChanged;
            if (h != null) h(null, EventArgs.Empty);
        }
    }
}
