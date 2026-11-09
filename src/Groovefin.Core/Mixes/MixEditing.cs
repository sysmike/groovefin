using Groovefin.Core.Api;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Mixes;

/// <summary>Creates and changes mixes with the editor (#401), for the Mixes page and a mix's own page.</summary>
public sealed class MixEditing(IMixEditor editor, ILibraryService library, IStringResources strings, MixStore store)
{
    /// <summary>
    /// Shows the editor for a new mix (<paramref name="existing"/> <see langword="null"/>) or a saved one and saves
    /// what the user chose. Returns the saved mix, or <see langword="null"/> when the user cancelled.
    /// </summary>
    public async Task<Mix?> EditAsync(Mix? existing)
    {
        var choices = new MixEditorViewModel(strings, await GenresAsync(), existing?.Rules);
        if (!await editor.ShowAsync(choices) || !choices.CanSave)
        {
            return null;
        }

        var mix = new Mix { Id = existing?.Id ?? Guid.NewGuid().ToString("N"), Rules = choices.Build() };
        store.Save(mix);
        return mix;
    }

    /// <summary>The genres to choose from; without the server, only "every genre".</summary>
    private async Task<IReadOnlyList<Models.Genre>> GenresAsync()
    {
        try
        {
            return await library.GetAlbumGenresAsync();
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException)
        {
            return [];
        }
    }
}
