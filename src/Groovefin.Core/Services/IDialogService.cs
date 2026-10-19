namespace Groovefin.Core.Services;

/// <summary>Modal dialogs (ContentDialog) for ViewModels.</summary>
public interface IDialogService
{
    /// <summary>Asks a yes/no question. Returns <see langword="true"/> when the primary button was chosen.</summary>
    Task<bool> ConfirmAsync(string title, string message, string primaryButtonText, string closeButtonText);

    /// <summary>Asks for one line of text. Returns <see langword="null"/> when cancelled.</summary>
    Task<string?> PromptAsync(string title, string label, string initialText, string primaryButtonText, string closeButtonText);
}
