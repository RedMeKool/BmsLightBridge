using System.Windows;

namespace BmsLightBridge
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Catch unhandled exceptions from UI thread
            DispatcherUnhandledException += (_, args) =>
            {
                LogException("DispatcherUnhandledException", args.Exception);
                args.Handled = true;
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nSee BmsLightBridge_errors.log for details.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // Catch unhandled exceptions from background threads
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                LogException("UnobservedTaskException", args.Exception);
                args.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    LogException("UnhandledException", ex);
            };
        }

        private static void LogException(string source, Exception ex)
        {
            try
            {
                string logPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "BmsLightBridge_errors.log");

                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n" +
                               $"{ex.GetType().FullName}: {ex.Message}\n" +
                               $"{ex.StackTrace}\n" +
                               (ex.InnerException != null
                                   ? $"--- Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}\n"
                                   : "") +
                               new string('-', 80) + "\n";

                System.IO.File.AppendAllText(logPath, entry);
            }
            catch { /* logging must never crash the app */ }
        }
    }
}
