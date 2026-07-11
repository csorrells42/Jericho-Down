using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace JerichoDown.Audio;

internal sealed class StaThreadDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _workItems = [];
    private readonly Thread _thread;
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
        if (Volatile.Read(ref _disposeRequested) != 0 && !ReferenceEquals(Thread.CurrentThread, _thread))
        {
            throw new ObjectDisposedException(GetType().Name);
        }

        if (ReferenceEquals(Thread.CurrentThread, _thread))
        {
            return action();
        }

        using var completed = new ManualResetEventSlim(false);
        Exception? exception = null;
        T? result = default;
        try
        {
            _workItems.Add(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    completed.Set();
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            throw new ObjectDisposedException(GetType().Name, ex);
        }

        completed.Wait();
        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return result!;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        _workItems.CompleteAdding();
        if (!ReferenceEquals(Thread.CurrentThread, _thread))
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void Run()
    {
        try
        {
            foreach (var workItem in _workItems.GetConsumingEnumerable())
            {
                workItem();
            }
        }
        finally
        {
            _workItems.Dispose();
        }
    }
}