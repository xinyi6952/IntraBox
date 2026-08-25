using System;
using System.Drawing;
using System.IO;
using Tesseract;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;

namespace IntraBox.Modules.Screenshot
{
    /// <summary>
    /// 本地 OCR：Tesseract 4.1（自带 tessdata_fast 的 chi_sim+eng）。
    /// 绿色目录运行，无网络。Win7 需已安装 Visual C++ 2015-2019 运行库。
    /// </summary>
    internal static class OcrService
    {
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
    }
}
