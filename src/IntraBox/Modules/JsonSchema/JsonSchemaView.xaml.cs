using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Controls;
using IntraBox.Core;
using Newtonsoft.Json.Linq;

namespace IntraBox.Modules.JsonSchema
{
    public partial class JsonSchemaView : UserControl, IModuleView
    {
        private readonly AsyncTaskGate _gate = new AsyncTaskGate();

        private void CancelPending()
        {
            _gate.Cancel();
            LoadingOverlay.Hide(this);
        }


        public JsonSchemaView() { InitializeComponent(); }
        public void OnActivated() { }
        public void OnDeactivated() { CancelPending(); }

        private async void Validate_Click(object sender, RoutedEventArgs e)
        {
            string json = JsonBox.Text ?? "";
            string schemaText = SchemaBox.Text ?? "";

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "校验中…");

            try
            {
                var result = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var data = JToken.Parse(json);
                    var schema = JToken.Parse(schemaText);
                    var errors = JsonSchemaUtil.Validate(data, schema, "$");
                    return new { Text = JsonSchemaUtil.FormatErrors(errors), Count = errors.Count };
                }, token);
                if (version != _gate.Version) return;
                OutBox.Text = result.Text;
                SetMsg(result.Count == 0 ? "通过" : "有 " + result.Count + " 处问题", result.Count > 0);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (version == _gate.Version) SetMsg(ex.RootMessage(), true); }
        }

        private async void Infer_Click(object sender, RoutedEventArgs e)
        {
            string json = JsonBox.Text ?? "";

            CancelPending();
            int version = _gate.Bump();
            var cts = new CancellationTokenSource();
            _gate.Current = cts;
            var token = cts.Token;
            LoadingOverlay.Show(this, "推导中…");

            string schema;
            try
            {
                schema = await Task.Run(() => JsonSchemaUtil.Infer(JToken.Parse(json)), token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (version == _gate.Version) SetMsg(ex.RootMessage(), true); return; }

            if (version != _gate.Version) return;
            SchemaBox.Text = schema;
            SetMsg("已生成 Schema", false);
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
            LoadingOverlay.Hide(this);
        }

    }
}
