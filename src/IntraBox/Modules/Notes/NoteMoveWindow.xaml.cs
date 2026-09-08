using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IntraBox.Modules.Notes
{
    public partial class NoteMoveWindow : Window
    {
        public string SelectedParentUid { get; private set; }

        public NoteMoveWindow()
        {
            InitializeComponent();
        }

        public static bool TryPick(Window owner, IList<NoteItem> items, string currentParentUid, out string parentUid)
        {
            parentUid = currentParentUid ?? "";
            var w = new NoteMoveWindow();
            w.Owner = owner;
            w.BuildTree(items, currentParentUid ?? "");
            if (w.ShowDialog() != true) return false;
            parentUid = w.SelectedParentUid ?? "";
            return true;
        }

        private void BuildTree(IList<NoteItem> items, string selectUid)
        {
            CatTree.Items.Clear();
            var root = MakeItem("根目录", "");
            root.IsExpanded = true;
            var rows = NoteTree.FlattenFolders(items);
            var stack = new List<TreeViewItem>();
            stack.Add(root);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || row.Item == null) continue;
                while (stack.Count > row.Depth + 1)
                    stack.RemoveAt(stack.Count - 1);
                var item = MakeItem(row.Item.Title, row.Item.Uid);
                item.IsExpanded = true;
                stack[stack.Count - 1].Items.Add(item);
                stack.Add(item);
            }
            CatTree.Items.Add(root);
            SelectByUid(root, selectUid);
        }

        private static TreeViewItem MakeItem(string name, string uid)
        {
            var item = new TreeViewItem();
            item.Header = name ?? "";
            item.Tag = uid ?? "";
            return item;
        }

        private static void SelectByUid(TreeViewItem item, string uid)
        {
            if (item == null) return;
            string tag = item.Tag as string ?? "";
            if (tag == uid)
            {
                item.IsSelected = true;
                item.BringIntoView();
                return;
            }
            for (int i = 0; i < item.Items.Count; i++)
                SelectByUid(item.Items[i] as TreeViewItem, uid);
        }

        private void CatTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            TryAccept();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            TryAccept();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TryAccept()
        {
            var item = CatTree.SelectedItem as TreeViewItem;
            if (item == null)
            {
                ErrorText.Text = "请选择一个目录。";
                return;
            }
            SelectedParentUid = item.Tag as string ?? "";
            DialogResult = true;
        }
    }
}
