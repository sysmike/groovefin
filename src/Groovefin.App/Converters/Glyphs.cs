namespace Groovefin.App.Converters;

/// <summary>x:Bind helpers for Segoe Fluent Icons glyphs that depend on state.</summary>
internal static class Glyphs
{
    /// <summary>Filled heart for favourites, outline otherwise.</summary>
    public static string Heart(bool isFavourite) => isFavourite ? "" : "";
}
