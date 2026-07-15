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
        e.Handled = false;
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
