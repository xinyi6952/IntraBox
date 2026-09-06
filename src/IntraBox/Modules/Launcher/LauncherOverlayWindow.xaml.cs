using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IntraBox.Core;

namespace IntraBox.Modules.Launcher
{
    public partial class LauncherOverlayWindow : Window
    {
        private static LauncherOverlayWindow _open;
        private List<LauncherHit> _catalog = new List<LauncherHit>();
        private bool _closing;

        public LauncherOverlayWindow()
        {
            InitializeComponent();
        }

        /// <summary>全局热键：已打开则关闭，否则弹出并销毁上次实例。</summary>
        public static void Toggle()
        {
            if (_open != null)
            {
                _open.Close();
                return;
            }
            var w = new LauncherOverlayWindow();
            _open = w;
            w.Closed += (s, e) => { if (_open == w) _open = null; };
            var wa = SystemParameters.WorkArea;
            w.Left = wa.Left + (wa.Width - w.Width) / 2;
            w.Top = wa.Top + (wa.Height - w.Height) / 3;
            w.Show();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LauncherStore.Reload();
            _catalog = LauncherCatalog.Build();
            RefreshHits();
            SearchBox.Focus();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (_closing) return;
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _closing = true;
            _catalog = null;
            HitsList.ItemsSource = null;
            base.OnClosed(e);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshHits();
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                if (HitsList.Items.Count == 0) return;
                HitsList.Focus();
                HitsList.SelectedIndex = 0;
                var item = HitsList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                if (item != null) item.Focus();
                return;
            }
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ExecuteSelectedOrFirst();
            }
        }

        private void HitsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ExecuteSelectedOrFirst();
            }
        }

        private void HitsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ExecuteSelectedOrFirst();
        }

        private void RefreshHits()
        {
            string q = SearchBox.Text;
            var recents = LauncherStore.SnapshotRecents();
            List<LauncherHit> hits;
            if (string.IsNullOrWhiteSpace(q))
                hits = LauncherSearch.EmptyQuery(_catalog, recents, LauncherSearch.EmptyQueryToolCap);
            else
                hits = LauncherSearch.Rank(_catalog, q, recents);
            HitsList.ItemsSource = hits;
            if (hits.Count > 0)
                HitsList.SelectedIndex = 0;
        }

        private void ExecuteSelectedOrFirst()
        {
            var hit = HitsList.SelectedItem as LauncherHit;
            if (hit == null && HitsList.Items.Count > 0)
                hit = HitsList.Items[0] as LauncherHit;
            if (hit == null) return;
            Execute(hit);
        }

        private void Execute(LauncherHit hit)
        {
            if (hit.Kind == LauncherHit.KindFavorite)
            {
                var fav = LauncherStore.GetFavorite(hit.Payload);
                _closing = true;
                Close();
                if (fav == null)
                {
                    MessageBox.Show("收藏已不存在。", "启动器", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!LauncherLaunch.ConfirmIfNeeded(fav, Application.Current != null ? Application.Current.MainWindow : null))
                    return;
                if (LauncherLaunch.TryOpen(fav))
                    LauncherStore.TouchRecent(hit.Id);
                return;
            }
            _closing = true;
            Close();
            LauncherStore.TouchRecent(hit.Id);
            if (hit.Kind == LauncherHit.KindTool)
            {
                OpenMain(hit.Payload, null);
                return;
            }
            if (hit.Kind == LauncherHit.KindTodo)
            {
                OpenMain("todo", hit.Payload);
                return;
            }
            if (hit.Kind == LauncherHit.KindNote)
            {
                OpenMain("notes", hit.Payload);
                return;
            }
            if (hit.Kind == LauncherHit.KindVault)
            {
                OpenMain("vault", hit.Payload);
                return;
            }
        }

        private static void OpenMain(string key, string uid)
        {
            var main = Application.Current != null ? Application.Current.MainWindow as MainWindow : null;
            if (main == null) return;
            if (key == "todo")
                main.OpenTodo(uid);
            else if (key == "notes")
                main.OpenNote(uid);
            else if (key == "vault")
                main.OpenVault(uid);
            else
                main.OpenTool(key);
        }
    }
}
