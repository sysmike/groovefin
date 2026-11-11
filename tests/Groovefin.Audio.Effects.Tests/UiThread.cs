using System.Collections.Concurrent;
using Groovefin.Core.Services;

namespace Groovefin.Audio.Effects.Tests;

/// <summary>
/// One thread that runs what is sent to it, in order: the app's UI thread for a test. The engine is called on
/// it and raises its events on it, while MediaPlayer's own threads only queue work and never wait for it.
/// </summary>
internal sealed class UiThread : IDispatcher, IDisposable
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;

    public UiThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Test UI thread" };
        _thread.Start();
    }

    public bool HasThreadAccess => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    public Task EnqueueAsync(Action action) => EnqueueAsync(() =>
    {
        action();
        return true;
    });

    public Task<T> EnqueueAsync<T>(Func<T> function)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Work()
        {
            try
            {
                done.SetResult(function());
            }
            catch (Exception ex)
            {
                // Handed to whoever awaits the work, as a dispatcher does.
                done.SetException(ex);
            }
        }

        try
        {
            _work.Add(Work);
        }
        catch (InvalidOperationException)
        {
            // The thread has ended: a late event of a player that is closing.
            done.SetCanceled();
        }

        return done.Task;
    }

    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(10));
        _work.Dispose();
    }

    private void Run()
    {
        foreach (var work in _work.GetConsumingEnumerable())
        {
            work();
        }
    }
}
