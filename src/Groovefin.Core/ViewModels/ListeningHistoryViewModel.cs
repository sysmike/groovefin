using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.History;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// The listening history in Settings (#382): clears the plays kept for On repeat and the statistics, after asking.
/// The server's play counts stay.
/// </summary>
public sealed partial class ListeningHistoryViewModel(PlayHistory history, IDialogService dialogs, IStringResources strings)
{
    [RelayCommand]
    private async Task ClearAsync()
    {
        if (await dialogs.ConfirmAsync(
                strings.GetString("History_ClearTitle"),
                strings.GetString("History_ClearMessage"),
                strings.GetString("History_ClearConfirm"),
                strings.GetString("Dialog_Cancel")))
        {
            await history.ClearAsync();
        }
    }
}
