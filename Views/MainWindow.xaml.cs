using System.Windows;

namespace BmsLightBridge.Views
{
    public partial class MainWindow : MahApps.Metro.Controls.MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is IDisposable disposable)
                disposable.Dispose();

            base.OnClosed(e);
        }
    }
}
