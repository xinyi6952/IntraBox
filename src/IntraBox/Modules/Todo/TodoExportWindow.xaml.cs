using System.Windows;

namespace IntraBox.Modules.Todo
{
    public partial class TodoExportWindow : Window
    {
        public const int ScopeAll = 0;
        public const int ScopeTodo = 1;
        public const int ScopeDone = 2;

        public int Scope { get; private set; }

        public TodoExportWindow()
        {
            InitializeComponent();
        }

        public void Prefill(int listTab)
        {
            if (listTab == 1)
                DoneRadio.IsChecked = true;
            else
                TodoRadio.IsChecked = true;
        }

        public string FileLabel
        {
            get
            {
                if (Scope == ScopeDone) return "已办";
                if (Scope == ScopeAll) return "全部";
                return "待办";
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (DoneRadio.IsChecked == true) Scope = ScopeDone;
            else if (AllRadio.IsChecked == true) Scope = ScopeAll;
            else Scope = ScopeTodo;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
