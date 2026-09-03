using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;

namespace IntraBox.Controls
{
    /// <summary>
    /// 结果筛选条：对 DataGrid 的行按可见文本做包含匹配（不区分大小写）实时过滤。
    /// 复用 ICollectionView.Filter，不改变底层数据。行对象可为匿名类型或具体类。
    /// </summary>
    public partial class GridFilterBar : UserControl
    {
        private DataGrid _grid;

        public GridFilterBar()
        {
            InitializeComponent();
        }

        /// <summary>绑定要过滤的 DataGrid，立即应用当前筛选。</summary>
        public void Attach(DataGrid grid)
        {
            _grid = grid;
            Apply();
        }

        /// <summary>当前筛选词。</summary>
        public string FilterText { get { return Box.Text ?? ""; } }

        private void Box_TextChanged(object sender, TextChangedEventArgs e)
        {
            Apply();
        }

        /// <summary>数据源刷新后（如重新赋值 ItemsSource）重新套用筛选。</summary>
        public void Apply()
        {
            if (_grid == null) return;
            var view = CollectionViewSource.GetDefaultView(_grid.ItemsSource);
            if (view == null) return;
            string kw = (Box.Text ?? "").Trim();
            if (kw.Length == 0)
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = o => Matches(o, kw);
            }
        }

        private static bool Matches(object row, string kw)
        {
            if (row == null) return false;
            // 遍历行的所有公开实例属性，任一属性值包含关键字即命中
            foreach (var p in row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead) continue;
                object v;
                try { v = p.GetValue(row, null); }
                catch { continue; }
                if (v == null) continue;
                if (v.ToString().IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
