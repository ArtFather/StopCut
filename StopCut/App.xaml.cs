using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace StopCut;

public partial class App : Application
{
    /// <summary>Where unexpected errors are written: %AppData%\StopCut\crash.log.</summary>
    public static string CrashLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StopCut", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report(args.ExceptionObject as Exception, "Unhandled error", fatal: true);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteLog(args.Exception, "Background task error");
            args.SetObserved();
        };
        base.OnStartup(e);

        // The license agreement must be accepted before the main window opens. Closing the dialog
        // must not end the app on its own, so shutdown is explicit until the main window is up.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!EulaWindow.IsAccepted() && new EulaWindow(viewOnly: false).ShowDialog() != true)
        {
            Shutdown();
            return;
        }
        MainWindow = new MainWindow();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep running when possible: log it, tell the user, carry on.
        e.Handled = true;
        Report(e.Exception, "Unexpected error", fatal: MainWindow is not { IsLoaded: true });
        if (MainWindow is not { IsLoaded: true }) Shutdown(1);
    }

    private static void Report(Exception? ex, string title, bool fatal)
    {
        WriteLog(ex, title);
        MessageBox.Show(
            $"{(fatal ? "StopCut could not start." : "Something went wrong, but the app will keep running.")}\n\n" +
            $"{ex?.GetBaseException().Message}\n\nDetails were saved to:\n{CrashLogPath}",
            "StopCut - " + title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void WriteLog(Exception? ex, string title)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            var sb = new StringBuilder()
                .AppendLine($"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {title}")
                .AppendLine($"Version: {typeof(App).Assembly.GetName().Version}")
                .AppendLine(ex?.ToString() ?? "(no exception details)")
                .AppendLine();
            File.AppendAllText(CrashLogPath, sb.ToString());
        }
        catch
        {
            // Nothing more we can do if the log can't be written.
        }
    }
}
