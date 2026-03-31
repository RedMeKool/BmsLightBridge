using System.Diagnostics;
using System.Windows;
using BmsLightBridge.ViewModels;
using Microsoft.Win32;

namespace BmsLightBridge.Views
{
    public partial class MainWindow : MahApps.Metro.Controls.MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.StartMinimized)
                WindowState = WindowState.Minimized;
        }

        private void MenuImportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title       = "Import Configuration",
                Filter      = "JSON config files (*.json)|*.json|All files (*.*)|*.*",
                FilterIndex = 1
            };

            if (dlg.ShowDialog() != true) return;

            if (DataContext is not MainViewModel vm) return;

            var result = MessageBox.Show(
                "Importing a configuration will replace all current mappings and settings.\n\nContinue?",
                "Import Configuration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            if (!vm.ImportConfig(dlg.FileName))
            {
                MessageBox.Show(
                    "The selected file could not be read as a valid BmsLightBridge configuration.",
                    "Import Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void MenuExportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title      = "Export Configuration",
                Filter     = "JSON config files (*.json)|*.json|All files (*.*)|*.*",
                FileName   = "BmsLightBridge_config.json",
                FilterIndex = 1
            };

            if (dlg.ShowDialog() != true) return;

            if (DataContext is MainViewModel vm)
                vm.ExportConfig(dlg.FileName);
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "BMS Light Bridge v1.0.5\n\n" +
                "Bridges Falcon BMS shared memory data to physical cockpit hardware.\n" +
                "Supports WinWing HID devices and Arduino-based lighting controllers.\n\n" +
                "© 2024 RedMeKool — github.com/RedMeKool/BmsLightBridge",
                "About BMS Light Bridge",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void MenuGitHub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "https://github.com/RedMeKool/BmsLightBridge",
                UseShellExecute = true
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is IDisposable disposable)
                disposable.Dispose();

            base.OnClosed(e);
        }
    }
}
