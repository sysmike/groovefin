using Groovefin.Core.Api;
using Groovefin.Core.Notifications;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Playback;

/// <summary>What starting an instant mix does to the queue.</summary>
public enum InstantMixMode
{
    /// <summary>The mix replaces the queue and starts playing.</summary>
    Replace,

    /// <summary>The mix is added to the end of the queue.</summary>
    Append,
}

/// <summary>
/// Instant mixes: Jellyfin picks <see cref="Size"/> tracks like an item (track, album, artist, playlist
/// or genre). The setting decides whether the mix replaces the queue or is added to it. Failures are
/// shown, an empty mix does nothing.
/// </summary>
public sealed class InstantMix(ILibraryService library, IPlayer player, ISettingsService settings, IErrorReporter errors)
{
    /// <summary>Tracks in a mix.</summary>
    public const int Size = 100;

    public async Task StartAsync(string itemId)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemId);
        try
        {
            var mix = await library.GetInstantMixAsync(itemId, Size);
            if (mix.Count == 0)
            {
                return;
            }

            if (settings.Current.Playback.InstantMix == InstantMixMode.Append)
            {
                player.AddToQueue(mix);
            }
            else
            {
                player.Play(mix);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            errors.Report(ex, Operations.Playback);
        }
    }
}
