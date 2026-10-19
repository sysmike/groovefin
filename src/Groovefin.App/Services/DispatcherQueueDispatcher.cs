using Groovefin.Core.Services;
using Microsoft.UI.Dispatching;

namespace Groovefin.App.Services;

/// <summary>
/// <see cref="IDispatcher"/> backed by the UI thread's <see cref="DispatcherQueue"/>.
/// Must be created on the UI thread.
/// </summary>
internal sealed class DispatcherQueueDispatcher : IDispatcher
{
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread()
        ?? throw new InvalidOperationException("The dispatcher must be created on the UI thread.");

    public bool HasThreadAccess => _queue.HasThreadAccess;

    public Task EnqueueAsync(Action action) =>
        EnqueueAsync(() =>
        {
            action();
            return true;
        });

    public Task<T> EnqueueAsync<T>(Func<T> function)
    {
        if (_queue.HasThreadAccess)
        {
            return Task.FromResult(function());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = _queue.TryEnqueue(() =>
        {
            try
            {
                completion.SetResult(function());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        if (!queued)
        {
            completion.SetException(new InvalidOperationException("The UI dispatcher queue is shut down."));
        }

        return completion.Task;
    }
}
