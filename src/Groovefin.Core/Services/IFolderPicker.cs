namespace Groovefin.Core.Services;

/// <summary>Lets the user choose a folder (#399).</summary>
public interface IFolderPicker
{
    /// <summary>Gets the chosen folder's path, or <see langword="null"/> when the user cancelled.</summary>
    Task<string?> PickFolderAsync();
}
