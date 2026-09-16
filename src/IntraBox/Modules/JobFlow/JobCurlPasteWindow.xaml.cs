using System;
using System.Windows;
using IntraBox.Core;

namespace IntraBox.Modules.JobFlow
{
    public partial class JobCurlPasteWindow : Window
    {
        public JobStep ResultStep { get; private set; }

        public JobCurlPasteWindow()
        {
            InitializeComponent();
        }

        private void Recognize_Click(object sender, RoutedEventArgs e)
        {
            var step = JobStep.Create(JobStepKind.Rest);
            string err;
            if (!JobHeaderHelper.TryApplyCurlText(step, CurlBox.Text, out err))
            {
                FormHint.Apply(HintText, err ?? "无法识别");
                return;
            }
            ResultStep = step;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
