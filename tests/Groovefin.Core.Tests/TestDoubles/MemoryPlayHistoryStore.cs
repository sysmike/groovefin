using Groovefin.Core.History;

namespace Groovefin.Core.Tests.TestDoubles;

/// <summary>The history files in memory: the key of the last save tells the tests where to look.</summary>
internal sealed class MemoryPlayHistoryStore : IPlayHistoryStore
{
    private PlayHistoryData? _preloaded;

    public Dictionary<string, PlayHistoryData> Saved { get; } = [];

    public string? LastKey { get; private set; }

    /// <summary>Makes the next load of an unknown key return <paramref name="data"/>.</summary>
    public void Preload(PlayHistoryData data) => _preloaded = data;

    public Task<PlayHistoryData?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (Saved.TryGetValue(key, out var data))
        {
            return Task.FromResult<PlayHistoryData?>(data);
        }

        var preloaded = _preloaded;
        _preloaded = null;
        return Task.FromResult(preloaded);
    }

    public Task SaveAsync(string key, PlayHistoryData data, CancellationToken cancellationToken = default)
    {
        Saved[key] = data;
        LastKey = key;
        return Task.CompletedTask;
    }
}
