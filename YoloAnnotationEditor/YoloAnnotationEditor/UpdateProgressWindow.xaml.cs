using System;
using System.Windows;

namespace YoloAnnotationEditor
{
    public partial class UpdateProgressWindow : Window
    {
        public UpdateProgressWindow()
        {
            InitializeComponent();
        }

        public void UpdateStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
            });
        }

        public void UpdateProgress(int percentage, string details = "")
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = percentage;
                PercentageText.Text = $"{percentage}%";
                if (!string.IsNullOrEmpty(details))
                {
                    DetailsText.Text = details;
                }
            });
        }

        public void SetIndeterminate(bool isIndeterminate)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.IsIndeterminate = isIndeterminate;
                if (isIndeterminate)
                {
                    PercentageText.Text = "";
                }
            });
        }
    }
}
