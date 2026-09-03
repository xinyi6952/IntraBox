using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;
using Microsoft.Win32;
using QRCoder;
using ZXing;
using ZXing.Common;

namespace IntraBox.Modules.QrCode
{
    /// <summary>
    /// 二维码生成：文本生成（QRCoder）并可保存 PNG；图片解码（ZXing.Net）。
    /// </summary>
    public partial class QrCodeView : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }

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
            CancelPending();
            HistoryManager.Save("qrcode", new Dictionary<string, object>
            {
                { "input", InputBox.Text ?? "" }
            });
            ClearBitmap();
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text ?? "";
            if (string.IsNullOrEmpty(text))
            {
                SetMsg("请输入要编码的文本", true);
                return;
            }

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "生成中…");

            Bitmap bmp;
            try
            {
                bmp = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using (var gen = new QRCodeGenerator())
                    using (var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q))
                    using (var qr = new QRCode(data))
                    {
                        return qr.GetGraphic(20);
                    }
                }, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("生成失败：" + ex.RootMessage(), true); return; }

            if (version != _gate.Version) { bmp.Dispose(); return; }

            ReplaceBitmap(bmp);
            PreviewImage.Source = ScreenCapture.ToBitmapSource(_qrBmp);
            SetMsg("已生成", false);
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
                SetMsg("保存失败：" + ex.RootMessage(), true);
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

        private async void Decode_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择含二维码的图片",
                Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            string path = dlg.FileName;
            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "解码中…");

            string resultText;
            try
            {
                resultText = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using (var bmp = new Bitmap(path))
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
                        return result == null ? null : result.Text;
                    }
                }, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg("解码失败：" + ex.RootMessage(), true); return; }

            if (version != _gate.Version) return;
            if (string.IsNullOrEmpty(resultText)) { SetMsg("未识别到二维码", true); return; }
            InputBox.Text = resultText;
            SetMsg("已解码", false);
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
            LoadingOverlay.Hide(this);
        }
    }
}
