using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Navigation;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// Sign-in page for one server. The password lives only in this ViewModel and is cleared after a
/// successful sign-in; only the access token is stored.
/// </summary>
public sealed partial class SignInViewModel(IServerManager servers, INavigationService navigation, IStringResources strings)
    : ObservableObject
{
    private ServerProfile? _server;

    [ObservableProperty]
    public partial string ServerName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ServerAddress { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string UserName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    /// <summary>Selects the server to sign in to: <paramref name="serverId"/>, or the active server.</summary>
    public void Initialize(string? serverId)
    {
        _server = (serverId is null ? null : servers.Servers.FirstOrDefault(s => s.Id == serverId)) ?? servers.ActiveServer;
        ServerName = _server?.Name ?? string.Empty;
        ServerAddress = _server?.Address.ToString().TrimEnd('/') ?? string.Empty;
        UserName = _server?.UserName ?? string.Empty;
        Password = string.Empty;
        Error = null;
    }

    private bool CanSignIn() => _server is not null && !string.IsNullOrWhiteSpace(UserName);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        Error = null;
        try
        {
            await servers.SignInAsync(_server!.Id, UserName, Password, cancellationToken);
            Password = string.Empty;
            navigation.NavigateTo(PageKeys.Home);
        }
        catch (JellyfinException ex)
        {
            Error = strings.GetString(ex switch
            {
                { IsUnauthorized: true } => "SignIn_WrongCredentials",
                { StatusCode: null } => "SignIn_Unreachable",
                _ => "SignIn_Failed",
            });
        }
    }

    [RelayCommand]
    private void ChooseServer() => navigation.NavigateTo(PageKeys.Servers);
}
