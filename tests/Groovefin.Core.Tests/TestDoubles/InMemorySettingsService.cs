using Groovefin.Core.Settings;

namespace Groovefin.Core.Tests.TestDoubles;

/// <summary>Settings kept in memory only.</summary>
internal sealed class InMemorySettingsService : ISettingsService
{
    public AppSettings Current { get; private set; } = new();

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Update(Func<AppSettings, AppSettings> update)
    {
        var old = Current;
        Current = update(old);
        if (Current != old)
        {
            Changed?.Invoke(this, new SettingsChangedEventArgs(old, Current));
        }
    }

    public Task FlushAsync() => Task.CompletedTask;
}
