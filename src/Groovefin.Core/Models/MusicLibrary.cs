namespace Groovefin.Core.Models;

/// <summary>A music library (Jellyfin collection of type "music") of the signed-in user.</summary>
public sealed record MusicLibrary(string Id, string Name);
