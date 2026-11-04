using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Groovefin.Core.Downloads;
using Groovefin.Core.Models;
using Groovefin.Core.Services;

namespace Groovefin.Core.Playback;

/// <summary>A row of a track list: marked while its track plays (#247), and while it is downloaded (#337).</summary>
public interface ICurrentTrackRow
{
    Track Track { get; }

    bool IsCurrent { get; set; }

    bool IsDownloaded { get; set; }
}

/// <summary>
/// The track that is playing, marked in the track lists (#247): a page's view model hands over its rows
/// with <see cref="Watch"/>, and they are marked again whenever the engine moves on or stops. The downloaded
/// tracks are marked too, again whenever the downloads change (#337).
/// </summary>
public sealed class CurrentTrack
{
    private readonly IPlaybackEngine _engine;
    private readonly IOfflineLibrary? _downloads;

    // Weak: a page's view model needs no unregistering. The rows getter may hold its owner; the table still lets it go.
    private readonly ConditionalWeakTable<object, Func<IEnumerable<ICurrentTrackRow>>> _lists = new();

    public CurrentTrack(IPlaybackEngine engine, IOfflineLibrary? downloads = null, IDispatcher? dispatcher = null)
    {
        _engine = engine;
        _downloads = downloads;
        engine.CurrentChanged += (_, _) => MarkAll();
        if (downloads is not null && dispatcher is not null)
        {
            // Raised on the download manager's thread; the rows belong to the UI.
            downloads.Changed += (_, _) => _ = dispatcher.EnqueueAsync(MarkAll);
        }
    }

    /// <summary>Gets the ID of the track loaded in the engine, or <see langword="null"/> when nothing is.</summary>
    public string? TrackId => _engine.Current?.Track.Id;

    /// <summary>Marks <paramref name="rows"/> now and again on every change, for as long as <paramref name="owner"/> lives.</summary>
    public void Watch(object owner, Func<IEnumerable<ICurrentTrackRow>> rows)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(rows);
        _lists.AddOrUpdate(owner, rows);
        Mark(rows());
    }

    /// <summary>
    /// Watches a page's list: marks its rows now, each row added later, and all of them on every change.
    /// Returns the list, for a property initializer.
    /// </summary>
    public ObservableCollection<T> Watch<T>(ObservableCollection<T> rows)
        where T : ICurrentTrackRow
    {
        ArgumentNullException.ThrowIfNull(rows);
        rows.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is { } added)
            {
                Mark(added.Cast<ICurrentTrackRow>());
            }
        };
        Watch(rows, () => rows.Cast<ICurrentTrackRow>());
        return rows;
    }

    /// <summary>Marks rows a watched list just got (a page loaded, a list was refilled).</summary>
    public void Mark(IEnumerable<ICurrentTrackRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var id = TrackId;
        foreach (var row in rows)
        {
            row.IsCurrent = id is not null && row.Track.Id == id;
            row.IsDownloaded = _downloads?.IsDownloaded(row.Track.Id) == true;
        }
    }

    private void MarkAll()
    {
        foreach (var (_, rows) in _lists)
        {
            Mark(rows());
        }
    }
}
