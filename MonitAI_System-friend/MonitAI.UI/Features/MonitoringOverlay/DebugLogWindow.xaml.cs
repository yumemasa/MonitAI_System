using System.Windows;

namespace MonitAI.UI.Features.MonitoringOverlay
{
    public partial class DebugLogWindow : Window
    {
        public DebugLogWindow()
        {
            InitializeComponent();
        }

        public void SetText(string text)
        {
            if (LogTextBox != null)
            {
                LogTextBox.Text = text;
                LogTextBox.ScrollToEnd();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Close ではなく Hide にして再利用する
            e.Cancel = true;
            this.Hide();
        }
    }
}
