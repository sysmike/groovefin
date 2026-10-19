using Groovefin.Core.Queue;

namespace Groovefin.App.Converters;

/// <summary>x:Bind helpers for Segoe Fluent Icons glyphs that depend on state.</summary>
internal static class Glyphs
{
    /// <summary>Filled heart for favourites, outline otherwise.</summary>
    public static string Heart(bool isFavourite) => isFavourite ? "" : "";

    /// <summary>Pause while playing, play otherwise.</summary>
    public static string PlayPause(bool isPlaying) => isPlaying ? "" : "";

    /// <summary>"Repeat one" for <see cref="RepeatMode.One"/>, "repeat all" otherwise (off is shown unchecked).</summary>
    public static string Repeat(RepeatMode mode) => mode == RepeatMode.One ? "" : "";

    /// <summary>Muted, or a speaker with zero to three waves for the volume (0–100).</summary>
    public static string Volume(bool isMuted, double volume) => isMuted || volume <= 0
        ? ""
        : volume < 34 ? "" : volume < 67 ? "" : "";
}
