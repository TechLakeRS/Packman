using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Packman.Services;
using Packman.ViewModels;

namespace Packman;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Without these, a throw off the UI thread takes the process down with no trace.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        ErrorReporter.Reported += ex => Report(ex, "Something went wrong.");

        ThemeService.Apply(AppServices.Settings.Settings.Theme);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Report(e.Exception, "Packman hit an unexpected error.");
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Terminating either way; at least leave a log behind.
        if (e.ExceptionObject is Exception ex) WriteCrashLog(ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        WriteCrashLog(e.Exception);
    }

    /// <summary>Logs the failure and tells the user.</summary>
    private static void Report(Exception ex, string headline)
    {
        var logPath = WriteCrashLog(ex);
        var detail = logPath == null ? "" : $"\n\nDetails were written to:\n{logPath}";

        MessageBox.Show(
            $"{headline}\n\n{ex.Message}{detail}",
            "Packman", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string? WriteCrashLog(Exception ex)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packman", "Logs", "Errors");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, $"error-{DateTime.Now:yyyy-MM-dd}.log");
            var entry = new StringBuilder()
                .AppendLine(new string('=', 80))
                .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                .AppendLine(ex.ToString())
                .AppendLine();

            File.AppendAllText(path, entry.ToString(), Encoding.UTF8);
            return path;
        }
        catch (Exception logFailure)
        {
            Debug.WriteLine($"Could not write the crash log: {logFailure.Message}");
            return null;
        }
    }
}
