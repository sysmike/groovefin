using Groovefin.Core.Services;
using Windows.Storage.Pickers;

namespace Groovefin.App.Services;

/// <summary><see cref="IFolderPicker"/> with the Windows folder picker, owned by the main window (#399).</summary>
internal sealed class WindowsFolderPicker(MainWindow window) : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };

        // Without it the picker refuses to open; the files in the folder do not matter.
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
