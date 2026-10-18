using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IntraBox.Core;

namespace IntraBox.Modules.FileOrganize
{
    public partial class OrganizePreviewWindow : Window
    {
        private readonly List<PreviewRow> _rows = new List<PreviewRow>();
        private List<FilePathTree.Node> _nodes = new List<FilePathTree.Node>();
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public OrganizePreviewWindow(IList<OrganizePlanItem> plan)
        {
            InitializeComponent();
            BuildRows(plan);
            _nodes = BuildTree();
            BindGrid();
            RefreshCount();
        }

        /// <summary>用户确认后要移动的文件。未确认或未勾选时为空。</summary>
        public IList<OrganizePlanItem> Chosen { get; private set; }

        private void BuildRows(IList<OrganizePlanItem> plan)
        {
            _rows.Clear();
            if (plan == null) return;
            for (int i = 0; i < plan.Count; i++)
            {
                var item = plan[i];
                if (item == null) continue;
                item.Selected = false;
                var row = new PreviewRow(item);
                row.PropertyChanged += (s, e) => RefreshCount();
                _rows.Add(row);
            }
            Title = "整理预览（" + _rows.Count + " 个文件，默认不勾选）";
        }

        private List<FilePathTree.Node> BuildTree()
        {
            var leaves = new List<FilePathTree.Leaf>();
            for (int i = 0; i < _rows.Count; i++)
            {
                PreviewRow row = _rows[i];
                string folder = row.Folder ?? "";
                string name = string.IsNullOrEmpty(row.Name) ? "未命名" : row.Name;
                string full = string.IsNullOrEmpty(folder) ? name : Path.Combine(folder, name);
                leaves.Add(new FilePathTree.Leaf
                {
                    FullPath = full,
                    Detail = row.FromPath,
                    Payload = row
                });
            }
            return FilePathTree.Build("", leaves);
        }

        private void BindGrid()
        {
            List<FilePathTree.FlatRow> flat = FilePathTree.Flatten(_nodes, _collapsed);
            var rows = new List<GridRow>();
            for (int i = 0; i < flat.Count; i++)
                rows.Add(GridRow.From(flat[i]));
            ResultGrid.ItemsSource = rows;
        }

        private void Expand_Click(object sender, MouseButtonEventArgs e)
        {
            var host = sender as FrameworkElement;
            var row = host != null ? host.DataContext as GridRow : null;
            if (row == null || string.IsNullOrEmpty(row.Key) || !row.HasChildren) return;
            if (!_collapsed.Add(row.Key)) _collapsed.Remove(row.Key);
            BindGrid();
            e.Handled = true;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            SetAll(true);
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            SetAll(false);
        }

        private void SetAll(bool on)
        {
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].Selected = on;
            BindGrid();
            RefreshCount();
        }

        private void RefreshCount()
        {
            int n = CountSelected();
            CountHint.Text = "已勾选 " + n + " / " + _rows.Count
                + " 个文件。只有勾选的才会被移动，未勾选的保持原位。";
        }

        private int CountSelected()
        {
            int n = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Selected) n++;
            }
            return n;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var list = new List<OrganizePlanItem>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Selected)
                    list.Add(_rows[i].Item);
            }
            if (list.Count == 0)
            {
                MessageBox.Show(
                    "还没有勾选任何文件。\n\n默认全部不勾选。只有勾选的文件会被移动；未勾选的不会改动。",
                    "未勾选文件",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            Chosen = list;
            DialogResult = true;
            Close();
        }

        public sealed class PreviewRow : INotifyPropertyChanged
        {
            private bool _selected;

            public PreviewRow(OrganizePlanItem item)
            {
                Item = item;
                _selected = false;
            }

            public OrganizePlanItem Item { get; private set; }
            public string Name { get { return Item.Name; } }
            public string Folder { get { return Item.Folder; } }
            public string FromPath { get { return Item.FromPath; } }

            public bool Selected
            {
                get { return _selected; }
                set
                {
                    if (_selected == value) return;
                    _selected = value;
                    Item.Selected = value;
                    if (PropertyChanged != null)
                        PropertyChanged(this, new PropertyChangedEventArgs("Selected"));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private sealed class GridRow
        {
            private readonly PreviewRow _row;

            private GridRow(PreviewRow row)
            {
                _row = row;
            }

            public string Key { get; set; }
            public bool HasChildren { get; set; }
            public string Name { get; set; }
            public string Detail { get; set; }
            public Thickness Indent { get; set; }
            public Visibility ExpanderVisibility { get; set; }
            public string ExpanderGlyph { get; set; }
            public Visibility CheckVisibility { get; set; }

            public bool Selected
            {
                get { return _row != null && _row.Selected; }
                set
                {
                    if (_row != null) _row.Selected = value;
                }
            }

            public static GridRow From(FilePathTree.FlatRow flat)
            {
                FilePathTree.Node node = flat.Node;
                bool file = node != null && node.IsFile;
                var row = node != null ? node.Payload as PreviewRow : null;
                return new GridRow(file ? row : null)
                {
                    Key = flat.Key,
                    HasChildren = flat.HasChildren,
                    Name = node != null ? node.Name : "",
                    Detail = file ? (node.Detail ?? "") : "",
                    Indent = new Thickness(flat.Depth * 16, 0, 0, 0),
                    ExpanderVisibility = flat.HasChildren ? Visibility.Visible : Visibility.Collapsed,
                    ExpanderGlyph = flat.Expanded ? "−" : "+",
                    CheckVisibility = file ? Visibility.Visible : Visibility.Collapsed
                };
            }
        }
    }
}
