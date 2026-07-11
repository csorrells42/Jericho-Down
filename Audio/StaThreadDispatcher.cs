using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace JerichoDown.Audio;

internal sealed class StaThreadDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _workItems = [];
    private readonly Thread _thread;
    private bool _disposed;

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(Thread.CurrentThread, _thread))
        {
            return action();
        }

        using var completed = new ManualResetEventSlim(false);
        Exception? exception = null;
        T? result = default;
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
        completed.Wait();
        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return result!;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workItems.CompleteAdding();
        if (!ReferenceEquals(Thread.CurrentThread, _thread))
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _workItems.Dispose();
    }

    private void Run()
    {
        foreach (var workItem in _workItems.GetConsumingEnumerable())
        {
            workItem();
        }
    }
}
