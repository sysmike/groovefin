namespace Groovefin.Core.Api;

/// <summary>
/// Tells a caller when a server request is tried again (#392), so a slow first connection can say what it is doing.
/// The caller watches a scope (<see cref="Watch"/>); the HTTP layer's retries call <see cref="Retrying"/>, which
/// reaches the watcher of the request's own async flow and nobody else.
/// </summary>
public static class RequestAttempts
{
    private static readonly AsyncLocal<Action<int>?> Watcher = new();

    /// <summary>
    /// Calls <paramref name="onAttempt"/> with the attempt's number (2 for the first retry) for each retry of a
    /// request made in this async flow until the returned scope is disposed.
    /// </summary>
    public static IDisposable Watch(Action<int> onAttempt)
    {
        ArgumentNullException.ThrowIfNull(onAttempt);
        var previous = Watcher.Value;
        Watcher.Value = onAttempt;
        return new Scope(previous);
    }

    /// <summary>A request is about to be tried for the <paramref name="attempt"/>th time. Called by the HTTP layer.</summary>
    public static void Retrying(int attempt) => Watcher.Value?.Invoke(attempt);

    private sealed class Scope(Action<int>? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Watcher.Value = previous;
            }
        }
    }
}
