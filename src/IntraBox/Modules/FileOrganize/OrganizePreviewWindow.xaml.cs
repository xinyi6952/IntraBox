using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace IntraBox.Modules.FileOrganize
{
    public partial class OrganizePreviewWindow : Window
    {
        private readonly ObservableCollection<PreviewRow> _rows = new ObservableCollection<PreviewRow>();

        public OrganizePreviewWindow(IList<OrganizePlanItem> plan)
        {
            InitializeComponent();
            BuildRows(plan);
            FileGrid.ItemsSource = _rows;
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

        private void Check_Click(object sender, RoutedEventArgs e)
        {
            RefreshCount();
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
    }
}
