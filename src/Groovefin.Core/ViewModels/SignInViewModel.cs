using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groovefin.Core.Api;
using Groovefin.Core.Navigation;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.ViewModels;

/// <summary>
/// Sign-in page for one server, with user name and password or with Quick Connect. The password lives
/// only in this ViewModel and is cleared after a successful sign-in; only the access token is stored.
/// Over plain HTTP the page says so, and before the first sign-in at that address the user has to choose
/// to send the password and the token unencrypted (#293), also to a server on the home network.
/// </summary>
public sealed partial class SignInViewModel(
    IServerManager servers,
    IAuthenticationService authentication,
    INavigationService navigation,
    IDialogService dialogs,
    IStringResources strings,
    TimeProvider time,
    ILogger<SignInViewModel> logger)
    : ObservableObject
{
    /// <summary>How often the server is asked whether the Quick Connect code was approved.</summary>
    public static readonly TimeSpan QuickConnectPollInterval = TimeSpan.FromSeconds(3);

    /// <summary>How long to wait for approval before the code is treated as expired.</summary>
    public static readonly TimeSpan QuickConnectTimeout = TimeSpan.FromMinutes(5);

    private ServerProfile? _server;

    [ObservableProperty]
    public partial string ServerName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ServerAddress { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the server is reached over plain HTTP, without encryption (#293).</summary>
    [ObservableProperty]
    public partial bool IsUnencrypted { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string UserName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    /// <summary>Gets a value indicating whether the server has Quick Connect enabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsQuickConnectButton))]
    [NotifyCanExecuteChangedFor(nameof(QuickConnectCommand))]
    public partial bool IsQuickConnectAvailable { get; private set; }

    /// <summary>Gets the code to approve in another client while Quick Connect is waiting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingForQuickConnect), nameof(ShowsQuickConnectButton))]
    public partial string? QuickConnectCode { get; private set; }

    public bool IsWaitingForQuickConnect => QuickConnectCode is not null;

    public bool ShowsQuickConnectButton => IsQuickConnectAvailable && QuickConnectCode is null;

    /// <summary>Selects the server to sign in to: <paramref name="serverId"/>, or the active server.</summary>
    public void Initialize(string? serverId)
    {
        _server = (serverId is null ? null : servers.Servers.FirstOrDefault(s => s.Id == serverId)) ?? servers.ActiveServer;
        ServerName = _server?.Name ?? string.Empty;
        ServerAddress = _server?.Address.ToString().TrimEnd('/') ?? string.Empty;
        IsUnencrypted = _server is { } server && Servers.ServerAddress.IsUnencrypted(server.Address);
        UserName = _server?.UserName ?? string.Empty;
        Password = string.Empty;
        Error = null;
        IsQuickConnectAvailable = false;
    }

    /// <summary>Asks the server whether Quick Connect is enabled; the option stays hidden otherwise.</summary>
    public async Task CheckQuickConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_server is not { } server)
        {
            return;
        }

        try
        {
            IsQuickConnectAvailable = await authentication.IsQuickConnectEnabledAsync(server.Address, cancellationToken);
        }
        catch (JellyfinException ex)
        {
            // Not an error for the user: the password sign-in still works and reports an unreachable server.
            LogQuickConnectCheckFailed(ex, server.Name);
            IsQuickConnectAvailable = false;
        }
    }

    private bool CanSignIn() => _server is not null && !string.IsNullOrWhiteSpace(UserName);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        Error = null;
        if (!await ConfirmUnencryptedAsync())
        {
            return;
        }

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

    private bool CanQuickConnect() => _server is not null && IsQuickConnectAvailable;

    /// <summary>
    /// Shows a code and waits until the user approves it in another client, then signs in.
    /// <c>QuickConnectCancelCommand</c> stops waiting.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanQuickConnect), IncludeCancelCommand = true)]
    private async Task QuickConnectAsync(CancellationToken cancellationToken)
    {
        var server = _server!;
        Error = null;
        if (!await ConfirmUnencryptedAsync())
        {
            return;
        }

        try
        {
            var request = await authentication.StartQuickConnectAsync(server.Address, cancellationToken);
            QuickConnectCode = request.Code;

            if (!await WaitForApprovalAsync(server, request.Secret, cancellationToken))
            {
                Error = strings.GetString("SignIn_QuickConnectExpired");
                return;
            }

            await servers.SignInWithQuickConnectAsync(server.Id, request.Secret, cancellationToken);
            Password = string.Empty;
            navigation.NavigateTo(PageKeys.Home);
        }
        catch (JellyfinException ex)
        {
            Error = strings.GetString(ex switch
            {
                { StatusCode: HttpStatusCode.NotFound } => "SignIn_QuickConnectExpired",
                { StatusCode: null } => "SignIn_Unreachable",
                _ => "SignIn_QuickConnectFailed",
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled by the user or by leaving the page.
        }
        finally
        {
            QuickConnectCode = null;
        }
    }

    /// <summary>
    /// Over plain HTTP, asks once per server and address whether to send the password (or the Quick Connect
    /// secret) and then the token unencrypted. Gets <see langword="true"/> when it may go ahead.
    /// </summary>
    private async Task<bool> ConfirmUnencryptedAsync()
    {
        if (_server is not { } server || !Servers.ServerAddress.IsUnencrypted(server.Address) || server.UnencryptedAccepted)
        {
            return true;
        }

        var accepted = await dialogs.ConfirmAsync(
            strings.GetString("SignIn_UnencryptedTitle"),
            strings.GetString("SignIn_UnencryptedMessage").Replace("{0}", server.Name, StringComparison.Ordinal),
            strings.GetString("SignIn_UnencryptedAccept"),
            strings.GetString("Dialog_Cancel"));
        if (accepted)
        {
            servers.AcceptUnencrypted(server.Id);
            _server = server with { UnencryptedAccepted = true };
        }

        return accepted;
    }

    /// <summary>Polls until approved (<see langword="true"/>) or <see cref="QuickConnectTimeout"/> passed.</summary>
    private async Task<bool> WaitForApprovalAsync(ServerProfile server, string secret, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(QuickConnectTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            while (!await authentication.IsQuickConnectApprovedAsync(server.Address, secret, linked.Token))
            {
                await Task.Delay(QuickConnectPollInterval, time, linked.Token);
            }

            return true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    [RelayCommand]
    private void ChooseServer() => navigation.NavigateTo(PageKeys.Servers);

    [LoggerMessage(Level = LogLevel.Information, Message = "Could not check whether Quick Connect is enabled on {Server}")]
    private partial void LogQuickConnectCheckFailed(Exception exception, string server);
}
