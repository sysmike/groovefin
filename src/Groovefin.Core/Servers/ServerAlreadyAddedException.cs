namespace Groovefin.Core.Servers;

/// <summary>The server to add is already in the list, under the same or another address (#345).</summary>
public sealed class ServerAlreadyAddedException : Exception
{
    public ServerAlreadyAddedException()
        : base("The server is already in the list.")
    {
    }

    public ServerAlreadyAddedException(string message)
        : base(message)
    {
    }

    public ServerAlreadyAddedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ServerAlreadyAddedException(ServerProfile existing)
        : base($"{existing.Name} at {existing.Address} is already in the list.")
    {
        Existing = existing;
    }

    /// <summary>Gets the server in the list, or <see langword="null"/> when not known.</summary>
    public ServerProfile? Existing { get; }
}
