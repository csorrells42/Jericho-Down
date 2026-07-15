using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace JerichoDown.Modules.Audio.Asio;

internal sealed class StaThreadDispatcher : IDisposable
{
    private readonly ManualResetEventSlim _dispatcherReady = new(false);
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;
    private Exception? _startupException;
    private int _disposeRequested;

    public StaThreadDispatcher(string name)
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = name
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcherReady.Wait();
        if (_startupException is not null)
        {
            ExceptionDispatchInfo.Capture(_startupException).Throw();
        }
    }

    public void Invoke(Action action)
    {
        Invoke<object?>(() =>
        {
            action();
            return null;
        });
    }

    public T Invoke<T>(Func<T> action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || Volatile.Read(ref _disposeRequested) != 0 && !dispatcher.CheckAccess())
        {
            throw new ObjectDisposedException(GetType().Name);
        }

        if (dispatcher.CheckAccess())
        {
            return action();
        }

        try
        {
            return dispatcher.Invoke(action);
        }
        catch (TaskCanceledException ex)
        {
            throw new ObjectDisposedException(GetType().Name, ex);
        }
        catch (InvalidOperationException ex) when (Volatile.Read(ref _disposeRequested) != 0)
        {
            throw new ObjectDisposedException(GetType().Name, ex);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        if (!ReferenceEquals(Thread.CurrentThread, _thread))
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _dispatcherReady.Dispose();
    }

    private void Run()
    {
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _dispatcher = dispatcher;
            _dispatcherReady.Set();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            _startupException = ex;
            _dispatcherReady.Set();
        }
        finally
        {
            _dispatcher = null;
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }
}
