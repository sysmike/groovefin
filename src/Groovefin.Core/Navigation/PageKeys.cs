namespace Groovefin.Core.Navigation;

/// <summary>
/// Keys that identify the pages of the app. Views register a page type per key.
/// </summary>
public static class PageKeys
{
    public const string Home = "home";
    public const string Albums = "albums";
    public const string Artists = "artists";
    public const string Songs = "songs";
    public const string Genres = "genres";
    public const string Playlists = "playlists";
    public const string Folders = "folders";
    public const string Favourites = "favourites";
    public const string Search = "search";
    public const string Settings = "settings";
    public const string Album = "album";
    public const string Artist = "artist";

    /// <summary>The sections shown in the main navigation, in display order.</summary>
    public static IReadOnlyList<string> MainSections { get; } =
        [Home, Albums, Artists, Songs, Genres, Playlists, Folders, Favourites];
}
