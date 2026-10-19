using Groovefin.Core.Services;

namespace Groovefin.Core.Tests.TestDoubles;

/// <summary>Holds dispatched work until <see cref="RunAll"/>, like a UI thread that has not run yet.</summary>
internal sealed class QueuedDispatcher : IDispatcher
{
    private readonly Queue<Action> _pending = new();

    public bool HasThreadAccess => false;

    public int Pending => _pending.Count;

    public Task EnqueueAsync(Action action)
    {
        _pending.Enqueue(action);
        return Task.CompletedTask;
    }

    public Task<T> EnqueueAsync<T>(Func<T> function) => throw new NotSupportedException();

    public void RunAll()
    {
        while (_pending.TryDequeue(out var action))
        {
            action();
        }
    }
}
