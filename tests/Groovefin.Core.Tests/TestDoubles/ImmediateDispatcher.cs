using Groovefin.Core.Services;

namespace Groovefin.Core.Tests.TestDoubles;

/// <summary>Runs dispatched work synchronously on the calling thread.</summary>
internal sealed class ImmediateDispatcher : IDispatcher
{
    public bool HasThreadAccess => true;

    public Task EnqueueAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task<T> EnqueueAsync<T>(Func<T> function) => Task.FromResult(function());
}
