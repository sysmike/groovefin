namespace Groovefin.Core.Notifications;

/// <summary>
/// Logs an exception and tells the user what failed. Every <c>catch</c> that does not rethrow ends here,
/// so failures are never silent.
/// </summary>
public interface IErrorReporter
{
    /// <summary>
    /// Reports <paramref name="exception"/>. <paramref name="operationKey"/> is a resource key that names
    /// what failed, e.g. <see cref="Operations.LoadAlbums"/> ("Couldn't load albums").
    /// Cancellation is not an error and is ignored.
    /// </summary>
    void Report(Exception exception, string operationKey);
}

/// <summary>Resource keys for <see cref="IErrorReporter.Report"/> titles.</summary>
public static class Operations
{
    public const string Unexpected = "Operation_Unexpected";
    public const string SaveSettings = "Operation_SaveSettings";
    public const string ConnectServer = "Operation_ConnectServer";
    public const string LoadAlbums = "Operation_LoadAlbums";
    public const string LoadHome = "Operation_LoadHome";
    public const string LoadAlbum = "Operation_LoadAlbum";
    public const string SetFavourite = "Operation_SetFavourite";
    public const string LoadArtists = "Operation_LoadArtists";
    public const string LoadArtist = "Operation_LoadArtist";
    public const string LoadLibraries = "Operation_LoadLibraries";
    public const string Playback = "Operation_Playback";
    public const string SavePlaylist = "Operation_SavePlaylist";
    public const string Search = "Operation_Search";
    public const string LoadPlaylists = "Operation_LoadPlaylists";
    public const string LoadPlaylist = "Operation_LoadPlaylist";
    public const string EditPlaylist = "Operation_EditPlaylist";
    public const string LoadFavourites = "Operation_LoadFavourites";
}
