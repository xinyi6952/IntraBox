using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.RegBrowse
{
    public partial class RegBrowseView : UserControl, IModuleView
    {
        public RegBrowseView() { InitializeComponent(); FilterBar.Attach(ValGrid); }

        public void OnActivated()
        {
            KeyTree.Items.Clear();
            AddRoot(Registry.CurrentUser, "HKEY_CURRENT_USER");
            AddRoot(Registry.LocalMachine, "HKEY_LOCAL_MACHINE");
        }

        public void OnDeactivated() { }

        private void AddRoot(RegistryKey key, string name)
        {
            var item = new TreeViewItem { Header = name, Tag = new Node { Hive = key, Path = "" } };
            item.Items.Add("...");
            item.Expanded += Item_Expanded;
            KeyTree.Items.Add(item);
        }

        private void Item_Expanded(object sender, RoutedEventArgs e)
        {
            var item = sender as TreeViewItem;
            if (item == null) return;
            if (item.Items.Count != 1 || !(item.Items[0] is string)) return;
            item.Items.Clear();
            var node = item.Tag as Node;
            if (node == null) return;
            try
            {
                RegistryKey k = string.IsNullOrEmpty(node.Path) ? node.Hive : node.Hive.OpenSubKey(node.Path, false);
                if (k == null) return;
                try
                {
                    foreach (var name in k.GetSubKeyNames())
                    {
                        string child = string.IsNullOrEmpty(node.Path) ? name : node.Path + "\\" + name;
                        var sub = new TreeViewItem
                        {
                            Header = name,
                            Tag = new Node { Hive = node.Hive, Path = child }
                        };
                        sub.Items.Add("...");
                        sub.Expanded += Item_Expanded;
                        item.Items.Add(sub);
                    }
                }
                finally
                {
                    if (!string.IsNullOrEmpty(node.Path) && k != null) k.Close();
                }
            }
            catch
            {
                item.Items.Add("(无法读取)");
            }
        }

        private void KeyTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = KeyTree.SelectedItem as TreeViewItem;
            if (item == null) return;
            var node = item.Tag as Node;
            if (node == null) return;
            string full = node.Hive.Name + (string.IsNullOrEmpty(node.Path) ? "" : "\\" + node.Path);
            PathText.Text = full;
            var rows = new List<ValRow>();
            try
            {
                RegistryKey k = string.IsNullOrEmpty(node.Path) ? node.Hive : node.Hive.OpenSubKey(node.Path, false);
                if (k == null) return;
                try
                {
                    foreach (var name in k.GetValueNames())
                    {
                        object v = k.GetValue(name);
                        var kind = k.GetValueKind(name);
                        rows.Add(new ValRow
                        {
                            Name = string.IsNullOrEmpty(name) ? "(默认)" : name,
                            Kind = kind.ToString(),
                            Data = Format(v, kind)
                        });
                    }
                }
                finally
                {
                    if (!string.IsNullOrEmpty(node.Path) && k != null) k.Close();
                }
            }
            catch (Exception ex)
            {
                PathText.Text = full + "  （" + ex.RootMessage() + "）";
            }
            ValGrid.ItemsSource = rows;
            FilterBar.Apply();
        }

        private static string Format(object v, RegistryValueKind kind)
        {
            if (v == null) return "";
            if (kind == RegistryValueKind.Binary && v is byte[])
                return BitConverter.ToString((byte[])v);
            if (kind == RegistryValueKind.MultiString && v is string[])
                return string.Join(" | ", (string[])v);
            return v.ToString();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            string err;
            ClipboardHelper.TrySetText(PathText.Text ?? "", out err);
        }

        private sealed class Node
        {
            public RegistryKey Hive;
            public string Path;
        }

        private sealed class ValRow
        {
            public string Name { get; set; }
            public string Kind { get; set; }
            public string Data { get; set; }
        }
    }
}
