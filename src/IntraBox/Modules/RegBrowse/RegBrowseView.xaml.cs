using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntraBox.Core;
using Microsoft.Win32;

namespace IntraBox.Modules.RegBrowse
{
    public partial class RegBrowseView : UserControl, IModuleView
    {
        private readonly object _regLock = new object();
        private int _selGen;

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

        private async void Item_Expanded(object sender, RoutedEventArgs e)
        {
            var item = sender as TreeViewItem;
            if (item == null) return;
            if (item.Items.Count != 1 || !(item.Items[0] is string)) return;
            if ((item.Items[0] as string) == "正在读取…") return;
            item.Items.Clear();
            item.Items.Add("正在读取…");
            var node = item.Tag as Node;
            if (node == null) return;
            RegistryKey hive = node.Hive;
            string path = node.Path;
            string[] names = null;
            string error = null;
            try
            {
                await Task.Run(() =>
                {
                    lock (_regLock)
                    {
                        RegistryKey k = null;
                        try
                        {
                            k = string.IsNullOrEmpty(path) ? hive : hive.OpenSubKey(path, false);
                            if (k == null) { error = "无法打开该项"; return; }
                            names = k.GetSubKeyNames();
                        }
                        catch (Exception ex) { error = ex.Message; }
                        finally
                        {
                            if (k != null && !string.IsNullOrEmpty(path)) k.Close();
                        }
                    }
                });
            }
            catch (Exception ex) { error = ex.Message; }
            item.Items.Clear();
            if (!string.IsNullOrEmpty(error) || names == null)
            {
                item.Items.Add("(无法读取" + (string.IsNullOrEmpty(error) ? ")" : "：" + error + ")"));
                return;
            }
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                string child = string.IsNullOrEmpty(path) ? name : path + "\\" + name;
                var sub = new TreeViewItem
                {
                    Header = name,
                    Tag = new Node { Hive = hive, Path = child }
                };
                sub.Items.Add("...");
                sub.Expanded += Item_Expanded;
                item.Items.Add(sub);
            }
        }

        private async void KeyTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = KeyTree.SelectedItem as TreeViewItem;
            if (item == null) return;
            var node = item.Tag as Node;
            if (node == null) return;
            int gen = ++_selGen;
            RegistryKey hive = node.Hive;
            string path = node.Path;
            string full = hive.Name + (string.IsNullOrEmpty(path) ? "" : "\\" + path);
            PathText.Text = full + "  （正在读取…）";
            ValGrid.ItemsSource = new List<ValRow>
            {
                new ValRow { Name = "正在读取…", Kind = "", Data = "" }
            };
            var rows = new List<ValRow>();
            string error = null;
            try
            {
                await Task.Run(() =>
                {
                    lock (_regLock)
                    {
                        RegistryKey k = null;
                        try
                        {
                            k = string.IsNullOrEmpty(path) ? hive : hive.OpenSubKey(path, false);
                            if (k == null) { error = "无法打开该项"; return; }
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
                        catch (Exception ex) { error = ex.Message; }
                        finally
                        {
                            if (k != null && !string.IsNullOrEmpty(path)) k.Close();
                        }
                    }
                });
            }
            catch (Exception ex) { error = ex.Message; }
            if (gen != _selGen) return;
            if (!string.IsNullOrEmpty(error))
                PathText.Text = full + "  （" + error + "）";
            else
                PathText.Text = full;
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
