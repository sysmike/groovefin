namespace Groovefin.Core.Services;

/// <summary>
/// Marshals work to the UI thread. Code outside views uses this instead of a platform dispatcher.
/// </summary>
public interface IDispatcher
{
    /// <summary>Gets a value indicating whether the caller is on the UI thread.</summary>
    bool HasThreadAccess { get; }

    /// <summary>Runs <paramref name="action"/> on the UI thread and completes when it has run.</summary>
    Task EnqueueAsync(Action action);

    /// <summary>Runs <paramref name="function"/> on the UI thread and returns its result.</summary>
    Task<T> EnqueueAsync<T>(Func<T> function);
}
