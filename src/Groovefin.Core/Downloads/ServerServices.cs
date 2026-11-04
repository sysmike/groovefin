using Groovefin.Core.Api;

namespace Groovefin.Core.Downloads;

/// <summary>
/// The server's own library and playlist services, without the offline answers from the downloads (#335). The
/// download manager uses them: it must see the server's collections, not what it has downloaded of them.
/// </summary>
public sealed record ServerServices(ILibraryService Library, IPlaylistService Playlists);
