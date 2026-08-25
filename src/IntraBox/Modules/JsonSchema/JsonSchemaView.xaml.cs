using System;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;
using Newtonsoft.Json.Linq;

namespace IntraBox.Modules.JsonSchema
{
    public partial class JsonSchemaView : UserControl, IModuleView
    {
        public JsonSchemaView() { InitializeComponent(); }
        public void OnActivated() { }
        public void OnDeactivated() { }

        private void Validate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var data = JToken.Parse(JsonBox.Text ?? "");
                var schema = JToken.Parse(SchemaBox.Text ?? "");
                var errors = JsonSchemaUtil.Validate(data, schema, "$");
                OutBox.Text = JsonSchemaUtil.FormatErrors(errors);
                SetMsg(errors.Count == 0 ? "通过" : "有 " + errors.Count + " 处问题", errors.Count > 0);
            }
            catch (Exception ex)
            {
                SetMsg(ex.Message, true);
            }
        }

        private void Infer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var data = JToken.Parse(JsonBox.Text ?? "");
                SchemaBox.Text = JsonSchemaUtil.Infer(data);
                SetMsg("已生成 Schema", false);
            }
            catch (Exception ex)
            {
                SetMsg(ex.Message, true);
            }
        }

        private void SetMsg(string t, bool err)
        {
            MsgText.Foreground = FindResource(err ? "DangerBrush" : "OkBrush") as System.Windows.Media.Brush;
            MsgText.Text = t;
        }
    }
}
