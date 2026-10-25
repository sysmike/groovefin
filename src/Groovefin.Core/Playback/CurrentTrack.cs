using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Groovefin.Core.Models;

namespace Groovefin.Core.Playback;

/// <summary>A row of a track list that is marked while its track plays (#247).</summary>
public interface ICurrentTrackRow
{
    Track Track { get; }

    bool IsCurrent { get; set; }
}

/// <summary>
/// The track that is playing, marked in the track lists (#247): a page's view model hands over its rows
/// with <see cref="Watch"/>, and they are marked again whenever the engine moves on or stops.
/// </summary>
public sealed class CurrentTrack
{
    private readonly IPlaybackEngine _engine;

    // Weak: a page's view model needs no unregistering. The rows getter may hold its owner; the table still lets it go.
    private readonly ConditionalWeakTable<object, Func<IEnumerable<ICurrentTrackRow>>> _lists = new();

    public CurrentTrack(IPlaybackEngine engine)
    {
        _engine = engine;
        engine.CurrentChanged += (_, _) => MarkAll();
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
