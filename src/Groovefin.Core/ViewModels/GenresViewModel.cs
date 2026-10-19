using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>A genre tile.</summary>
public sealed record GenreItem(Genre Genre)
{
    public string Name => Genre.Name;
}

/// <summary>Genres page: the genres of the music library's albums, by name.</summary>
public sealed partial class GenresViewModel(
    ILibraryService library,
    INavigationService navigation,
    IErrorReporter errors) : ObservableObject
{
    public ObservableCollection<GenreItem> Genres { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool LoadFailed { get; private set; }

    /// <summary>Gets a value indicating whether the library has no genres (after a successful load).</summary>
    public bool IsEmpty => !IsLoading && !LoadFailed && Genres.Count == 0;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        LoadFailed = false;
        try
        {
            var genres = await library.GetAlbumGenresAsync(cancellationToken);
            Genres.Clear();
            foreach (var genre in genres)
            {
                Genres.Add(new GenreItem(genre));
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            LoadFailed = true;
            errors.Report(ex, Operations.LoadGenres);
        }
        catch (OperationCanceledException)
        {
            // The page was closed.
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Open(GenreItem item) => navigation.NavigateTo(PageKeys.Genre, item.Genre.Id);
}
