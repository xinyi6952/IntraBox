using System;
using System.Drawing;
using System.IO;
using IntraBox.Core;
using Tesseract;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;

namespace IntraBox.Modules.Screenshot
{
    /// <summary>
    /// 本地 OCR：Tesseract 4.1（自带 tessdata_fast 的 chi_sim+eng）。
    /// 绿色目录运行，无网络。Win7 需已安装 Visual C++ 2015-2019 运行库。
    /// TesseractEngine 非线程安全：进程内同时只允许一次识别（OcrBusyHelper + EngineLock）。
    /// </summary>
    internal static class OcrService
    {
        private static readonly object EngineLock = new object();

        internal static bool TryBegin()
        {
            return OcrBusyHelper.TryBegin();
        }

        internal static void End()
        {
            OcrBusyHelper.End();
        }

        public static string Recognize(Bitmap bmp)
        {
            if (bmp == null) throw new ArgumentNullException("bmp");
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            if (!Directory.Exists(dir))
                throw new InvalidOperationException("缺少 tessdata 目录，无法识别文字。");

            bool chi = File.Exists(Path.Combine(dir, "chi_sim.traineddata"));
            bool eng = File.Exists(Path.Combine(dir, "eng.traineddata"));
            if (!chi && !eng)
                throw new InvalidOperationException("缺少语言包（chi_sim / eng.traineddata）。");

            string lang = chi && eng ? "chi_sim+eng" : (chi ? "chi_sim" : "eng");

            lock (EngineLock)
            {
                try
                {
                    using (var engine = new TesseractEngine(dir, lang, EngineMode.LstmOnly))
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, DrawingImageFormat.Png);
                        byte[] bytes = ms.ToArray();
                        using (var pix = Pix.LoadFromMemory(bytes))
                        using (var page = engine.Process(pix))
                        {
                            string text = page.GetText();
                            return text == null ? "" : text.Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (WinCompatHelper.LooksLikeMissingVcRuntime(ex))
                        throw new InvalidOperationException(WinCompatHelper.OcrVcRuntimeHint, ex);
                    throw;
                }
            }
        }

        internal static void LogFailure(Exception ex, Bitmap bmp)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            string tess = Path.Combine(baseDir, "tessdata");
            int w = bmp != null ? bmp.Width : 0;
            int h = bmp != null ? bmp.Height : 0;
            AppLog.Error(
                "OCR 失败 base=" + baseDir
                + " tessdata=" + Directory.Exists(tess)
                + " chi_sim=" + File.Exists(Path.Combine(tess, "chi_sim.traineddata"))
                + " eng=" + File.Exists(Path.Combine(tess, "eng.traineddata"))
                + " tesseract41=" + File.Exists(Path.Combine(baseDir, "x64", "tesseract41.dll"))
                + " leptonica=" + File.Exists(Path.Combine(baseDir, "x64", "leptonica-1.80.0.dll"))
                + " size=" + w + "x" + h,
                ex);
        }
    }
}
