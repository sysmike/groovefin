using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Mixes;

/// <summary>Shows the rules of a mix for editing (#401); the app shows a dialog.</summary>
public interface IMixEditor
{
    /// <summary>Shows <paramref name="editor"/>; returns <see langword="true"/> when the user saved.</summary>
    Task<bool> ShowAsync(MixEditorViewModel editor);
}
