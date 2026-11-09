using Groovefin.Core.Servers;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Mixes;

/// <summary>
/// The saved mixes of the signed-in user on the active server (#401), kept in the settings like the favourite
/// playlists' order. Raises <see cref="Changed"/> after a save or a delete, and when another user is signed in.
/// </summary>
public sealed class MixStore : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IServerManager _servers;

    public MixStore(ISettingsService settings, IServerManager servers)
    {
        _settings = settings;
        _servers = servers;
        _servers.Changed += OnServersChanged;
    }

    public event EventHandler? Changed;

    /// <summary>Gets the mixes, by name; none while nobody is signed in.</summary>
    public IReadOnlyList<Mix> Mixes => Owner is { } owner && _settings.Current.Browse.Mixes.TryGetValue(owner, out var mixes)
        ? [.. mixes.OrderBy(m => m.Rules.Name, StringComparer.CurrentCultureIgnoreCase)]
        : [];

    /// <summary>The server and user the mixes belong to, or <see langword="null"/> when nobody is signed in.</summary>
    private string? Owner => _servers.IsSignedIn && _servers.ActiveServer is { } server ? $"{server.Id}/{server.UserId}" : null;

    public Mix? Find(string id) => Mixes.FirstOrDefault(m => m.Id == id);

    /// <summary>Adds the mix, or replaces the one with its ID.</summary>
    /// <exception cref="InvalidOperationException">Nobody is signed in.</exception>
    public void Save(Mix mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        var owner = Owner ?? throw new InvalidOperationException("A mix needs a signed-in user.");
        Update(owner, mixes => [.. mixes.Where(m => m.Id != mix.Id), mix]);
    }

    public void Delete(string id)
    {
        if (Owner is { } owner)
        {
            Update(owner, mixes => [.. mixes.Where(m => m.Id != id)]);
        }
    }

    public void Dispose() => _servers.Changed -= OnServersChanged;

    private void Update(string owner, Func<IReadOnlyList<Mix>, IReadOnlyList<Mix>> change)
    {
        _settings.Update(s =>
        {
            var all = new Dictionary<string, IReadOnlyList<Mix>>(s.Browse.Mixes, StringComparer.Ordinal);
            var changed = change(all.TryGetValue(owner, out var mixes) ? mixes : []);
            if (changed.Count == 0)
            {
                all.Remove(owner);
            }
            else
            {
                all[owner] = changed;
            }

            return s with { Browse = s.Browse with { Mixes = all } };
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnServersChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
