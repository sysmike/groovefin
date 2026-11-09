using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Mixes;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>A row of the Mixes page (#401): the mix's name and its rules in a line.</summary>
public sealed record MixItem(Mix Mix, string Summary, string PlayName)
{
    public string Name => Mix.Rules.Name;

    /// <summary>Gets the text Narrator reads: the name and the rules.</summary>
    public string AccessibleName => $"{Name}, {Summary}";
}

/// <summary>
/// The Mixes page (#401): the user's rule-based mixes. Make a new one, open, play, change or delete one. A mix's
/// songs are picked anew each time it plays or opens.
/// </summary>
public sealed partial class MixesViewModel : ObservableObject, IDisposable
{
    private readonly MixStore _store;
    private readonly MixEditing _editing;
    private readonly MixBuilder _builder;
    private readonly IPlayer _player;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IErrorReporter _errors;
    private readonly IStringResources _strings;
    private readonly IDispatcher _dispatcher;

    public MixesViewModel(
        MixStore store,
        MixEditing editing,
        MixBuilder builder,
        IPlayer player,
        INavigationService navigation,
        IDialogService dialogs,
        IErrorReporter errors,
        IStringResources strings,
        IDispatcher dispatcher)
    {
        _store = store;
        _editing = editing;
        _builder = builder;
        _player = player;
        _navigation = navigation;
        _dialogs = dialogs;
        _errors = errors;
        _strings = strings;
        _dispatcher = dispatcher;
        _store.Changed += OnStoreChanged;
        Refresh();
    }

    public ObservableCollection<MixItem> Mixes { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    public void Dispose() => _store.Changed -= OnStoreChanged;

    /// <summary>Makes a new mix with the editor and opens it.</summary>
    [RelayCommand]
    private async Task NewAsync()
    {
        if (await _editing.EditAsync(null) is { } mix)
        {
            _navigation.NavigateTo(PageKeys.Mix, mix.Id);
        }
    }

    [RelayCommand]
    private void Open(MixItem item) => _navigation.NavigateTo(PageKeys.Mix, item.Mix.Id);

    /// <summary>Picks the mix's songs and plays them.</summary>
    [RelayCommand]
    private async Task PlayAsync(MixItem item)
    {
        try
        {
            var tracks = await _builder.BuildAsync(item.Mix.Rules);
            if (tracks.Count > 0)
            {
                _player.Play(tracks);
            }
        }
        catch (Exception ex) when (ex is JellyfinException or InvalidOperationException or ArgumentException)
        {
            _errors.Report(ex, Operations.Playback);
        }
    }

    [RelayCommand]
    private Task EditAsync(MixItem item) => _editing.EditAsync(item.Mix);

    /// <summary>Deletes the mix after asking. Its songs stay where they are: a mix only holds rules.</summary>
    [RelayCommand]
    private async Task DeleteAsync(MixItem item)
    {
        if (await MixDeletion.ConfirmAsync(_dialogs, _strings, item.Name))
        {
            _store.Delete(item.Mix.Id);
        }
    }

    /// <summary>A sign-in elsewhere raises this on a thread-pool thread; the list is bound to the UI.</summary>
    private void OnStoreChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.HasThreadAccess)
        {
            Refresh();
        }
        else
        {
            _ = _dispatcher.EnqueueAsync(Refresh);
        }
    }

    private void Refresh()
    {
        Mixes.Clear();
        foreach (var mix in _store.Mixes)
        {
            var play = _strings.GetString("Mixes_PlayName").Replace("{0}", mix.Rules.Name, StringComparison.Ordinal);
            Mixes.Add(new MixItem(mix, MixSummary.Describe(mix.Rules, _strings), play));
        }

        IsEmpty = Mixes.Count == 0;
    }
}

/// <summary>The question before a mix is deleted, on the Mixes page and the mix's own page.</summary>
internal static class MixDeletion
{
    public static Task<bool> ConfirmAsync(IDialogService dialogs, IStringResources strings, string name) =>
        dialogs.ConfirmAsync(
            strings.GetString("Mix_DeleteTitle"),
            strings.GetString("Mix_DeleteMessage").Replace("{0}", name, StringComparison.Ordinal),
            strings.GetString("Mix_DeleteConfirm"),
            strings.GetString("Dialog_Cancel"));
}
