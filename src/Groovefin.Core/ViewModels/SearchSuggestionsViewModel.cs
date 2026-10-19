using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.ViewModels;

/// <summary>What a search suggestion leads to.</summary>
public enum SuggestionKind
{
    Artist,
    Album,
    Track,
}

/// <summary>One entry under the search box.</summary>
public sealed record SearchSuggestion(SuggestionKind Kind, string Id, string Title, string Subtitle, Track? Track = null)
{
    /// <summary>Gets the text Narrator reads, e.g. "Discovery, Album, Daft Punk".</summary>
    public string AccessibleName { get; init; } = string.Join(", ", new[] { Title, Subtitle }.Where(s => s.Length > 0));

    /// <summary>Gets the title; the search box puts it into the text box when the suggestion is chosen.</summary>
    public override string ToString() => Title;
}

/// <summary>
/// Live suggestions for the title bar's search box: a few artists, albums and tracks for the typed text,
/// asked for <see cref="Debounce"/> after the last key press. A newer text cancels the pending request.
/// Failures only clear the list (and are logged); the results page reports errors.
/// </summary>
public sealed partial class SearchSuggestionsViewModel(
    ISearchService search,
    IPlayer player,
    INavigationService navigation,
    IStringResources strings,
    TimeProvider time,
    ILogger<SearchSuggestionsViewModel> logger) : ObservableObject, IDisposable
{
    /// <summary>Suggestions per kind.</summary>
    public const int PerKind = 3;

    /// <summary>Shorter texts get no suggestions; they match too much to help.</summary>
    public const int MinLength = 2;

    /// <summary>Quiet time after the last key press before suggestions are asked for.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _pending;

    public ObservableCollection<SearchSuggestion> Suggestions { get; } = [];

    /// <summary>The user typed <paramref name="text"/>; completes when its suggestions are shown or it was superseded.</summary>
    public async Task UpdateAsync(string text)
    {
        Cancel();
        var term = text?.Trim() ?? string.Empty;
        if (term.Length < MinLength)
        {
            return;
        }

        var pending = new CancellationTokenSource();
        _pending = pending;
        try
        {
            await Task.Delay(Debounce, time, pending.Token);
            var results = await search.SearchAsync(term, PerKind, pending.Token);
            if (!pending.IsCancellationRequested)
            {
                Show(results);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer text or a submitted search replaced this one.
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            LogSuggestionsFailed(ex);
            Suggestions.Clear();
        }
    }

    /// <summary>Drops pending and shown suggestions, e.g. when the search is submitted.</summary>
    public void Cancel()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
        Suggestions.Clear();
    }

    /// <summary>Opens the artist or album, or plays the track.</summary>
    public void Choose(SearchSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        Cancel();
        switch (suggestion.Kind)
        {
            case SuggestionKind.Artist:
                navigation.NavigateTo(PageKeys.Artist, suggestion.Id);
                break;
            case SuggestionKind.Album:
                navigation.NavigateTo(PageKeys.Album, suggestion.Id);
                break;
            case SuggestionKind.Track when suggestion.Track is { } track:
                player.Play([track]);
                break;
            default:
                break;
        }
    }

    public void Dispose() => Cancel();

    private void Show(SearchResults results)
    {
        Suggestions.Clear();
        var artistKind = strings.GetString("Search_KindArtist");
        var albumKind = strings.GetString("Search_KindAlbum");
        var trackKind = strings.GetString("Search_KindTrack");

        foreach (var artist in results.Artists)
        {
            Suggestions.Add(new SearchSuggestion(SuggestionKind.Artist, artist.Id, artist.Name, artistKind)
            {
                AccessibleName = $"{artist.Name}, {artistKind}",
            });
        }

        foreach (var album in results.Albums)
        {
            var subtitle = album.AlbumArtist ?? string.Empty;
            Suggestions.Add(new SearchSuggestion(SuggestionKind.Album, album.Id, album.Name, subtitle)
            {
                AccessibleName = Join(album.Name, albumKind, subtitle),
            });
        }

        foreach (var track in results.Tracks)
        {
            var subtitle = string.Join(", ", track.Artists.Select(a => a.Name));
            Suggestions.Add(new SearchSuggestion(SuggestionKind.Track, track.Id, track.Name, subtitle, track)
            {
                AccessibleName = Join(track.Name, trackKind, subtitle),
            });
        }
    }

    private static string Join(params string[] parts) => string.Join(", ", parts.Where(s => s.Length > 0));

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loading search suggestions failed")]
    private partial void LogSuggestionsFailed(Exception exception);
}
