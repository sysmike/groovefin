namespace Groovefin.Core.Services;

/// <summary>Modal dialogs (ContentDialog) for ViewModels.</summary>
public interface IDialogService
{
    /// <summary>Asks a yes/no question. Returns <see langword="true"/> when the primary button was chosen.</summary>
    Task<bool> ConfirmAsync(string title, string message, string primaryButtonText, string closeButtonText);

    /// <summary>Asks for one line of text. Returns <see langword="null"/> when cancelled.</summary>
    Task<string?> PromptAsync(string title, string label, string initialText, string primaryButtonText, string closeButtonText);

    /// <summary>Shows read-only details as label/value rows (e.g. a track's info).</summary>
    Task ShowDetailsAsync(string title, IReadOnlyList<KeyValuePair<string, string>> details, string closeButtonText);
}
