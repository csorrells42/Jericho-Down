using System.Windows;
using System.Windows.Threading;

namespace JerichoDown;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppStateStore.BeginRun();
        DispatcherUnhandledException += AppDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskSchedulerUnobservedTaskException;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppStateStore.MarkCleanShutdown();
        DispatcherUnhandledException -= AppDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskSchedulerUnobservedTaskException;
        base.OnExit(e);
    }

    private static void AppDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppStateStore.LogUnhandledException("dispatcher-unhandled-exception", e.Exception);
        NotifyMainWindow(e.Exception);

        // Keep the app running for volunteers operating it live instead of crashing the
        // whole session over a single UI-thread exception (e.g. a device hiccup mid-recording).
        // The exception is already logged to diagnostics.log for follow-up.
        e.Handled = true;
    }

    private static void NotifyMainWindow(Exception exception)
    {
        try
        {
            if (Current?.MainWindow is EqualizerWindow mainWindow)
            {
                mainWindow.NotifyRecoveredFromUnhandledError(exception);
            }
        }
        catch
        {
            // Never let status-text notification turn a handled exception into a crash.
        }
    }

    private static void CurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppStateStore.LogUnhandledException("appdomain-unhandled-exception", exception);
        }
        else
        {
            AppStateStore.LogDiagnostic("appdomain-unhandled-exception", e.ExceptionObject?.ToString() ?? "Unknown exception object.");
        }
    }

    private static void TaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppStateStore.LogUnhandledException("task-unobserved-exception", e.Exception);
        e.SetObserved();
    }
}
