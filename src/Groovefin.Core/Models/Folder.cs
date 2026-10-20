namespace Groovefin.Core.Models;

/// <summary>A folder of the music library as it is on disk (or a whole music library at the top).</summary>
public sealed record Folder(string Id, string Name);

/// <summary>What a folder holds directly: its subfolders and its tracks, each by name.</summary>
public sealed record FolderContent(IReadOnlyList<Folder> Folders, IReadOnlyList<Track> Tracks);
