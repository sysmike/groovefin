using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Navigation;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>One row of the server list.</summary>
public sealed record ServerItem(ServerProfile Profile, bool IsActive)
{
    public string Name => Profile.Name;

    public string Address => Profile.Address.ToString().TrimEnd('/');

    public string? Version => Profile.Version;
}

/// <summary>
/// Servers page: add, edit address, remove and switch servers. Problems with what the user typed are
/// shown inline on the page (<see cref="FormError"/>), not as global notifications.
/// </summary>
public sealed partial class ServersViewModel : ObservableObject, IDisposable
{
    private readonly IServerManager _servers;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IStringResources _strings;

    public ServersViewModel(IServerManager servers, IDialogService dialogs, INavigationService navigation, IStringResources strings)
    {
        _servers = servers;
        _dialogs = dialogs;
        _navigation = navigation;
        _strings = strings;
        _servers.Changed += OnServersChanged;
        Refresh();
    }

    public ObservableCollection<ServerItem> Servers { get; } = [];

    public bool HasServers => Servers.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewAddress { get; set; } = string.Empty;

    /// <summary>Gets the last add/edit error for the inline InfoBar, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormError))]
    public partial string? FormError { get; private set; }

    public bool HasFormError => FormError is not null;

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewAddress);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync(CancellationToken cancellationToken)
    {
        FormError = null;
        if (await TryAsync(() => _servers.AddAsync(NewAddress, cancellationToken)))
        {
            NewAddress = string.Empty;

            // First-run flow: go on as soon as there is a server.
            if (Servers.Count == 1)
            {
                // TODO(#15): continue to sign-in for the new server.
                _navigation.NavigateTo(PageKeys.Home);
            }
        }
    }

    [RelayCommand]
    private async Task EditAddressAsync(ServerItem item)
    {
        FormError = null;
        var address = await _dialogs.PromptAsync(
            _strings.GetString("Servers_EditTitle"),
            _strings.GetString("Servers_AddressLabel"),
            item.Address,
            _strings.GetString("Dialog_Save"),
            _strings.GetString("Dialog_Cancel"));

        if (!string.IsNullOrWhiteSpace(address) && address != item.Address)
        {
            await TryAsync(() => _servers.UpdateAddressAsync(item.Profile.Id, address));
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(ServerItem item)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            _strings.GetString("Servers_RemoveTitle"),
            _strings.GetString("Servers_RemoveMessage").Replace("{0}", item.Name, StringComparison.Ordinal),
            _strings.GetString("Servers_RemoveConfirm"),
            _strings.GetString("Dialog_Cancel"));

        if (confirmed)
        {
            _servers.Remove(item.Profile.Id);
        }
    }

    [RelayCommand]
    private void SetActive(ServerItem item)
    {
        if (!item.IsActive)
        {
            _servers.SetActive(item.Profile.Id);
        }
    }

    public void Dispose() => _servers.Changed -= OnServersChanged;

    private async Task<bool> TryAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (ArgumentException)
        {
            FormError = _strings.GetString("Servers_InvalidAddress");
        }
        catch (InvalidOperationException)
        {
            FormError = _strings.GetString("Servers_DifferentServer");
        }
        catch (JellyfinException ex)
        {
            FormError = _strings.GetString(ex.StatusCode is null ? "Servers_Unreachable" : "Servers_NotJellyfin");
        }

        return false;
    }

    private void OnServersChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var activeId = _servers.ActiveServer?.Id;
        Servers.Clear();
        foreach (var profile in _servers.Servers)
        {
            Servers.Add(new ServerItem(profile, profile.Id == activeId));
        }

        OnPropertyChanged(nameof(HasServers));
    }
}
