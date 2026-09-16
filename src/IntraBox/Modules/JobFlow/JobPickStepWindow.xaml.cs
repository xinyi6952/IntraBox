using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using IntraBox.Core;

namespace IntraBox.Modules.JobFlow
{
    public partial class JobPickStepWindow : Window
    {
        public JobStep Picked { get; private set; }

        public JobPickStepWindow()
        {
            InitializeComponent();
            var src = JobStepLibStore.Snapshot();
            var rows = new List<LibRow>();
            for (int i = 0; i < src.Count; i++)
            {
                var s = src[i];
                if (s == null) continue;
                rows.Add(new LibRow
                {
                    Step = s,
                    Name = s.Name,
                    KindText = JobStep.KindText(s.Kind)
                });
            }
            LibGrid.ItemsSource = rows;
            if (rows.Count > 0) LibGrid.SelectedIndex = 0;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Accept();
        }

        private void LibGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Accept();
        }

        private void Accept()
        {
            var row = LibGrid.SelectedItem as LibRow;
            if (row == null || row.Step == null)
            {
                MessageBox.Show("请先选中一个步骤。", "步骤库", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Picked = row.Step;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private sealed class LibRow
        {
            public JobStep Step { get; set; }
            public string Name { get; set; }
            public string KindText { get; set; }
        }
    }
}
