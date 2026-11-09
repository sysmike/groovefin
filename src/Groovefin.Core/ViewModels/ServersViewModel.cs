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

    /// <summary>Gets a value indicating whether the server is reached over plain HTTP, without encryption (#293).</summary>
    public bool IsUnencrypted => ServerAddress.IsUnencrypted(Profile.Address);
}

/// <summary>A Jellyfin server found on the local network that is not added yet (#405).</summary>
public sealed record DiscoveredServerItem(DiscoveredServer Server, string AddName)
{
    public string Name => Server.Name;

    public string Address => Server.Address.ToString().TrimEnd('/');

    /// <summary>Gets a value indicating whether the server is reached over plain HTTP, without encryption (#293).</summary>
    public bool IsUnencrypted => ServerAddress.IsUnencrypted(Server.Address);

    /// <summary>Gets the row's name for Narrator: the server's name and address.</summary>
    public string AccessibleName => $"{Name}, {Address}";
}

/// <summary>
/// Servers page: add, edit address, remove and switch servers, and add a server found on the local network. Problems with what the user typed are
/// shown inline on the page (<see cref="FormError"/>), not as global notifications.
/// </summary>
public sealed partial class ServersViewModel : ObservableObject, IDisposable
{
    private readonly IServerManager _servers;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IStringResources _strings;
    private readonly IDispatcher _dispatcher;
    private readonly IServerDiscovery _discovery;
    private bool _connecting;
    private IReadOnlyList<DiscoveredServer> _discovered = [];

    public ServersViewModel(
        IServerManager servers,
        IDialogService dialogs,
        INavigationService navigation,
        IStringResources strings,
        IDispatcher dispatcher,
        IServerDiscovery discovery)
    {
        _servers = servers;
        _dialogs = dialogs;
        _navigation = navigation;
        _strings = strings;
        _dispatcher = dispatcher;
        _discovery = discovery;
        _servers.Changed += OnServersChanged;
        AddCommand.PropertyChanged += OnConnectCommandChanged;
        AddDiscoveredCommand.PropertyChanged += OnConnectCommandChanged;
        Refresh();
    }

    public ObservableCollection<ServerItem> Servers { get; } = [];

    public bool HasServers => Servers.Count > 0;

    /// <summary>Gets the servers found on the local network that are not in <see cref="Servers"/> (#405).</summary>
    public ObservableCollection<DiscoveredServerItem> Discovered { get; } = [];

    public bool HasDiscovered => Discovered.Count > 0;

    /// <summary>Gets a value indicating whether a search has ended without a server to add.</summary>
    [ObservableProperty]
    public partial bool NoneDiscovered { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewAddress { get; set; } = string.Empty;

    /// <summary>Gets the last add/edit error for the inline InfoBar, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormError))]
    public partial string? FormError { get; private set; }

    public bool HasFormError => FormError is not null;

    /// <summary>Gets what a slow connection is doing (#392), e.g. "No answer yet. Trying again (attempt 2)…", or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttempt))]
    public partial string? AttemptText { get; private set; }

    public bool HasAttempt => AttemptText is not null;

    /// <summary>Gets a value indicating whether a server is being added: the page shows Cancel (#392).</summary>
    public bool IsConnecting => AddCommand.IsRunning || AddDiscoveredCommand.IsRunning;

    /// <summary>Stops adding a server; the address stays in the box.</summary>
    [RelayCommand]
    private void CancelConnect()
    {
        AddCommand.Cancel();
        AddDiscoveredCommand.Cancel();
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewAddress);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync(CancellationToken cancellationToken)
    {
        if (await AddAddressAsync(NewAddress, cancellationToken))
        {
            NewAddress = string.Empty;
        }
    }

    /// <summary>Looks for servers on the local network; the page starts it when it opens (#405).</summary>
    [RelayCommand]
    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        NoneDiscovered = false;
        try
        {
            _discovered = await _discovery.DiscoverAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RefreshDiscovered();
        NoneDiscovered = !HasDiscovered;
    }

    /// <summary>Adds a server found on the network at the address it gave, like a typed one.</summary>
    [RelayCommand]
    private Task AddDiscoveredAsync(DiscoveredServerItem item, CancellationToken cancellationToken) =>
        AddAddressAsync(item.Address, cancellationToken);

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
            if (!_servers.IsSignedIn)
            {
                _navigation.NavigateTo(PageKeys.SignIn, item.Profile.Id);
            }
        }
    }

    public void Dispose()
    {
        _servers.Changed -= OnServersChanged;
        AddCommand.PropertyChanged -= OnConnectCommandChanged;
        AddDiscoveredCommand.PropertyChanged -= OnConnectCommandChanged;
    }

    private async Task<bool> AddAddressAsync(string address, CancellationToken cancellationToken)
    {
        FormError = null;
        ServerProfile? added = null;
        bool ok;
        _connecting = true;
        AttemptText = _strings.GetString("Servers_Connecting");
        using (RequestAttempts.Watch(OnAttempt))
        {
            try
            {
                ok = await TryAsync(async () => added = await _servers.AddAsync(address, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                // Cancelled while it waited: the address stays in the box to change or try again.
                ok = false;
            }
            finally
            {
                _connecting = false;
                AttemptText = null;
            }
        }

        if (!ok)
        {
            return false;
        }

        // First-run flow: sign in to the first server right away.
        if (Servers.Count == 1)
        {
            _navigation.NavigateTo(PageKeys.SignIn, added!.Id);
        }

        return true;
    }

    /// <summary>The connection is tried again; the retry runs on the HTTP layer's thread.</summary>
    private void OnAttempt(int attempt)
    {
        var text = _strings.GetString("Servers_Retrying").Replace("{0}", attempt.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal);
        _ = _dispatcher.EnqueueAsync(() =>
        {
            // A retry reported just before the connection ended changes nothing any more.
            if (_connecting)
            {
                AttemptText = text;
            }
        });
    }

    private void OnConnectCommandChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAsyncRelayCommand.IsRunning))
        {
            OnPropertyChanged(nameof(IsConnecting));
        }
    }

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
        catch (ServerAlreadyAddedException ex)
        {
            FormError = ex.Existing is { } existing
                ? _strings.GetString("Servers_AlreadyAdded").Replace("{0}", existing.Name, StringComparison.Ordinal)
                : _strings.GetString("Servers_AlreadyAdded");
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

    /// <summary>The server manager raises this after network calls, on a thread-pool thread; the list is bound to the UI.</summary>
    private void OnServersChanged(object? sender, EventArgs e)
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
        var activeId = _servers.ActiveServer?.Id;
        Servers.Clear();
        foreach (var profile in _servers.Servers)
        {
            Servers.Add(new ServerItem(profile, profile.Id == activeId));
        }

        OnPropertyChanged(nameof(HasServers));
        RefreshDiscovered();
    }

    /// <summary>Lists the found servers that are not added yet, by ID or by address.</summary>
    private void RefreshDiscovered()
    {
        Discovered.Clear();
        foreach (var server in _discovered)
        {
            var added = _servers.Servers.Any(p =>
                string.Equals(p.Id, server.Id, StringComparison.OrdinalIgnoreCase)
                || Uri.Compare(p.Address, server.Address, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0);
            if (!added)
            {
                var addName = _strings.GetString("Servers_AddFound").Replace("{0}", server.Name, StringComparison.Ordinal);
                Discovered.Add(new DiscoveredServerItem(server, addName));
            }
        }

        OnPropertyChanged(nameof(HasDiscovered));
    }
}
