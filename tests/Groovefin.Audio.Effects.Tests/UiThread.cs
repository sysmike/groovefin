using System.Collections.Concurrent;
using Groovefin.Core.Services;

namespace Groovefin.Audio.Effects.Tests;

/// <summary>
/// One thread that runs what is sent to it, in order: the app's UI thread for a test. The engine is called on
/// it and raises its events on it, while MediaPlayer's own threads only queue work and never wait for it.
/// </summary>
internal sealed class UiThread : IDispatcher, IDisposable
{
    private readonly BlockingCollection<Task> _work = [];
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
        // Not started: the thread runs it, and what it throws goes to whoever awaits it, as with a dispatcher.
        var work = new Task<T>(function);
        try
        {
            _work.Add(work);
        }
        catch (InvalidOperationException)
        {
            // The thread has ended: a late event of a player that is closing.
            return Task.FromCanceled<T>(new CancellationToken(canceled: true));
        }

        return work;
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
            work.RunSynchronously();
        }
    }
}
