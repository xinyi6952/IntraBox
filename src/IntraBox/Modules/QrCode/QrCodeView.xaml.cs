using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;
using Microsoft.Win32;
using QRCoder;
using ZXing;
using ZXing.Common;

namespace IntraBox.Modules.QrCode
{
    /// <summary>
    /// 二维码：文本生成（QRCoder）并可保存 PNG；图片解码（ZXing.Net）。
    /// </summary>
    public partial class QrCodeView : UserControl, IModuleView
    {
        private Bitmap _qrBmp;

        public QrCodeView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            Dictionary<string, object> state;
            if (!HistoryManager.TryLoad("qrcode", out state)) return;
            InputBox.Text = HistoryManager.GetString(state, "input");
        }

        public void OnDeactivated()
        {
            HistoryManager.Save("qrcode", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" }
            });
            ClearBitmap();
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text ?? "";
            if (string.IsNullOrEmpty(text))
            {
                SetMsg("请输入要编码的文本", true);
                return;
            }
            try
            {
                using (var gen = new QRCodeGenerator())
                using (var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q))
                using (var qr = new QRCode(data))
                {
                    var bmp = qr.GetGraphic(20);
                    ReplaceBitmap(bmp);
                    PreviewImage.Source = ScreenCapture.ToBitmapSource(_qrBmp);
                }
                SetMsg("已生成", false);
            }
            catch (Exception ex)
            {
                SetMsg("生成失败：" + ex.Message, true);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_qrBmp == null)
            {
                SetMsg("请先生成二维码", true);
                return;
            }
            var dlg = new SaveFileDialog
            {
                Title = "保存二维码",
                Filter = "PNG 图片|*.png",
                FileName = "qrcode.png"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _qrBmp.Save(dlg.FileName, ImageFormat.Png);
                SetMsg("已保存 " + dlg.FileName, false);
            }
            catch (Exception ex)
            {
                SetMsg("保存失败：" + ex.Message, true);
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (_qrBmp == null)
            {
                SetMsg("请先生成二维码", true);
                return;
            }
            string err;
            var src = ScreenCapture.ToBitmapSource(_qrBmp);
            if (ClipboardHelper.TrySetImage(src, out err))
                SetMsg("已复制图片", false);
            else
                SetMsg(err, true);
        }

        private void Decode_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择含二维码的图片",
                Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                using (var bmp = new Bitmap(dlg.FileName))
                {
                    var reader = new BarcodeReader
                    {
                        AutoRotate = true,
                        Options = new DecodingOptions
                        {
                            TryHarder = true,
                            PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                        }
                    };
                    var result = reader.Decode(bmp);
                    if (result == null || string.IsNullOrEmpty(result.Text))
                    {
                        SetMsg("未识别到二维码", true);
                        return;
                    }
                    InputBox.Text = result.Text;
                    SetMsg("已解码", false);
                }
            }
            catch (Exception ex)
            {
                SetMsg("解码失败：" + ex.Message, true);
            }
        }

        private void ReplaceBitmap(Bitmap bmp)
        {
            ClearBitmap();
            _qrBmp = bmp;
        }

        private void ClearBitmap()
        {
            if (_qrBmp == null) return;
            _qrBmp.Dispose();
            _qrBmp = null;
            if (PreviewImage != null) PreviewImage.Source = null;
        }

        private void SetMsg(string text, bool error)
        {
            MsgText.Foreground = FindResource(error ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = text;
        }
    }
}
