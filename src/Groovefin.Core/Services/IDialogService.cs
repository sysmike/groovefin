namespace Groovefin.Core.Services;

/// <summary>Modal dialogs (ContentDialog) for ViewModels.</summary>
public interface IDialogService
{
    /// <summary>Asks a yes/no question. Returns <see langword="true"/> when the primary button was chosen.</summary>
    Task<bool> ConfirmAsync(string title, string message, string primaryButtonText, string closeButtonText);

    /// <summary>Asks a question with two answers and a way out. Returns the button chosen; the primary one is the default.</summary>
    Task<DialogChoice> ChooseAsync(string title, string message, string primaryButtonText, string secondaryButtonText, string closeButtonText);

    /// <summary>Asks for one line of text. Returns <see langword="null"/> when cancelled.</summary>
    Task<string?> PromptAsync(string title, string label, string initialText, string primaryButtonText, string closeButtonText);

    /// <summary>Shows read-only details as label/value rows under headings (e.g. a track's info). Values can be selected and copied.</summary>
    Task ShowDetailsAsync(string title, IReadOnlyList<DetailSection> sections, string closeButtonText);
}

/// <summary>The button chosen in <see cref="IDialogService.ChooseAsync"/>.</summary>
public enum DialogChoice
{
    /// <summary>The close button, Esc or the window closing.</summary>
    None,
    Primary,
    Secondary,
}

/// <summary>A heading and its label/value rows in <see cref="IDialogService.ShowDetailsAsync"/>.</summary>
public sealed record DetailSection(string Heading, IReadOnlyList<KeyValuePair<string, string>> Rows);
