using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using IntraBox.Core;

namespace IntraBox.Modules.FileOrganize
{
    public partial class OrganizePreviewWindow : Window
    {
        private readonly ObservableCollection<PreviewRow> _rows = new ObservableCollection<PreviewRow>();

        public OrganizePreviewWindow(IList<OrganizePlanItem> plan)
        {
            InitializeComponent();
            BuildRows(plan);
            FileTree.ItemsSource = BuildTree();
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

        private List<PreviewNode> BuildTree()
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
            List<FilePathTree.Node> nodes = FilePathTree.Build("", leaves);
            var list = new List<PreviewNode>();
            for (int i = 0; i < nodes.Count; i++)
                list.Add(Wrap(nodes[i]));
            return list;
        }

        private static PreviewNode Wrap(FilePathTree.Node n)
        {
            var row = n.Payload as PreviewRow;
            var node = new PreviewNode();
            node.Name = n.Name;
            node.Detail = n.IsFile ? ("来源 " + (n.Detail ?? "")) : "";
            node.IsFile = n.IsFile;
            node.Row = row;
            node.Children = new List<PreviewNode>();
            if (row != null)
            {
                row.PropertyChanged += delegate(object s, PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == "Selected") node.RaiseSelected();
                };
            }
            for (int i = 0; i < n.Children.Count; i++)
                node.Children.Add(Wrap(n.Children[i]));
            return node;
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

        public sealed class PreviewNode : INotifyPropertyChanged
        {
            public string Name { get; set; }
            public string Detail { get; set; }
            public bool IsFile { get; set; }
            public PreviewRow Row { get; set; }
            public List<PreviewNode> Children { get; set; }

            public bool Selected
            {
                get { return Row != null && Row.Selected; }
                set
                {
                    if (Row == null) return;
                    Row.Selected = value;
                }
            }

            public void RaiseSelected()
            {
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("Selected"));
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
