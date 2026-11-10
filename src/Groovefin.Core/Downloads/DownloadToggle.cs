using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Settings;

namespace Groovefin.Core.Downloads;

/// <summary>
/// The Download toggle of an album, playlist or artist page (#387): on downloads that item alone, without making it
/// a favourite; off deletes its files unless something else still wants them. It follows a change from a menu.
/// Use it on the UI thread.
/// </summary>
public sealed partial class DownloadToggle : ObservableObject
{
    private readonly IDownloadChoices _choices;
    private readonly DownloadItemKind _kind;
    private string? _id;
    private string _name = string.Empty;

    public DownloadToggle(IDownloadChoices choices, DownloadItemKind kind)
    {
        _choices = choices;
        _kind = kind;
    }

    /// <summary>Gets a value indicating whether the item is known, so the toggle can change.</summary>
    public bool CanChange => _id is not null;

    /// <summary>Gets or sets a value indicating whether the item is chosen to download.</summary>
    public bool IsOn
    {
        get => _id is not null && _choices.IsChosen(_kind, _id);
        set
        {
            if (_id is not null && value != IsOn)
            {
                _choices.SetChosen(_kind, _id, _name, value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The page's item is loaded; a change from a menu comes through <paramref name="messenger"/>.</summary>
    public void Show(string id, string name, IMessenger messenger)
    {
        ArgumentNullException.ThrowIfNull(messenger);

        // Weak: the page's view model needs no unregistering.
        if (!messenger.IsRegistered<DownloadChoiceChangedMessage>(this))
        {
            messenger.Register<DownloadToggle, DownloadChoiceChangedMessage>(this, static (r, m) => r.OnChoiceChanged(m));
        }

        _id = id;
        _name = name;
        OnPropertyChanged(nameof(CanChange));
        OnPropertyChanged(nameof(IsOn));
    }

    private void OnChoiceChanged(DownloadChoiceChangedMessage message)
    {
        if (message.Kind == _kind && message.Id == _id)
        {
            OnPropertyChanged(nameof(IsOn));
        }
    }
}
