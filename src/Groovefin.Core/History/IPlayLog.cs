namespace Groovefin.Core.History;

/// <summary>The signed-in user's plays, as the play history keeps them.</summary>
public interface IPlayLog
{
    /// <summary>
    /// Gets a copy of the signed-in user's plays, with what is known of their songs (#382). Not signed in, it is
    /// empty and starts now.
    /// </summary>
    Task<PlayLog> GetLogAsync();
}
