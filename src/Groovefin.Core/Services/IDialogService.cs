namespace Groovefin.Core.Services;

/// <summary>Modal dialogs (ContentDialog) for ViewModels.</summary>
public interface IDialogService
{
    /// <summary>Asks a yes/no question. Returns <see langword="true"/> when the primary button was chosen.</summary>
    Task<bool> ConfirmAsync(string title, string message, string primaryButtonText, string closeButtonText);

    /// <summary>Asks for one line of text. Returns <see langword="null"/> when cancelled.</summary>
    Task<string?> PromptAsync(string title, string label, string initialText, string primaryButtonText, string closeButtonText);

    /// <summary>Shows read-only details as label/value rows under headings (e.g. a track's info). Values can be selected and copied.</summary>
    Task ShowDetailsAsync(string title, IReadOnlyList<DetailSection> sections, string closeButtonText);
}

/// <summary>A heading and its label/value rows in <see cref="IDialogService.ShowDetailsAsync"/>.</summary>
public sealed record DetailSection(string Heading, IReadOnlyList<KeyValuePair<string, string>> Rows);
