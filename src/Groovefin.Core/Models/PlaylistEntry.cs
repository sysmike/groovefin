namespace Groovefin.Core.Models;

/// <summary>
/// One entry of a playlist. <see cref="EntryId"/> identifies the entry, not the track: a track can be in
/// a playlist more than once, and removing or moving works on entries.
/// </summary>
public sealed record PlaylistEntry(string EntryId, Track Track);
