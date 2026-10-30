using System.Net;
using Groovefin.Core.Api;
using Groovefin.Core.Navigation;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class SignInViewModelTests
{
    // Plain HTTP, already accepted: the tests of the sign-in itself do not meet the warning (#293).
    private static readonly ServerProfile Alpha = new() { Id = "id-a", Name = "Alpha", Address = new Uri("http://alpha:8096/"), UserName = "mike", UnencryptedAccepted = true };
    private static readonly ServerProfile Beta = new() { Id = "id-b", Name = "Beta", Address = new Uri("http://beta:8096/"), UnencryptedAccepted = true };
    private static readonly ServerProfile Plain = new() { Id = "id-p", Name = "Plain", Address = new Uri("http://192.168.1.10:8096/"), UserName = "mike" };
    private static readonly ServerProfile Secure = new() { Id = "id-s", Name = "Secure", Address = new Uri("https://media.example.com/"), UserName = "mike" };

    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly FakeTimeProvider _time = new();

    public SignInViewModelTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _servers.Servers.Returns([Alpha, Beta, Plain, Secure]);
        _servers.ActiveServer.Returns(Alpha);
    }

    private SignInViewModel CreateSut(string? serverId = null)
    {
        var sut = new SignInViewModel(_servers, _authentication, _navigation, _dialogs, _strings, _time, NullLogger<SignInViewModel>.Instance);
        sut.Initialize(serverId);
        return sut;
    }

    [Fact]
    public void Defaults_to_the_active_server_and_its_last_user()
    {
        var sut = CreateSut();

        sut.ServerName.Should().Be("Alpha");
        sut.ServerAddress.Should().Be("http://alpha:8096");
        sut.UserName.Should().Be("mike");
    }

    [Fact]
    public void Uses_the_requested_server()
    {
        var sut = CreateSut("id-b");

        sut.ServerName.Should().Be("Beta");
        sut.UserName.Should().BeEmpty();
        sut.SignInCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Successful_sign_in_clears_the_password_and_opens_home()
    {
        var sut = CreateSut();
        sut.Password = "secret";

        await sut.SignInCommand.ExecuteAsync(null);

        await _servers.Received(1).SignInAsync("id-a", "mike", "secret", Arg.Any<CancellationToken>());
        sut.Password.Should().BeEmpty();
        _navigation.Received(1).NavigateTo(PageKeys.Home);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "SignIn_WrongCredentials")]
    [InlineData(HttpStatusCode.InternalServerError, "SignIn_Failed")]
    [InlineData(null, "SignIn_Unreachable")]
    public async Task Failures_are_shown_on_the_page(HttpStatusCode? status, string expectedKey)
    {
        _servers.SignInAsync(default!, default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(_ => throw new JellyfinException("x", status));
        var sut = CreateSut();

        await sut.SignInCommand.ExecuteAsync(null);

        sut.Error.Should().Be(expectedKey);
        sut.HasError.Should().BeTrue();
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!, default);
    }

    [Fact]
    public async Task Over_plain_http_it_asks_before_sending_the_password()
    {
        _strings.GetString("SignIn_UnencryptedMessage").Returns("{0} is unencrypted");
        var sut = CreateSut("id-p");
        sut.Password = "secret";

        sut.IsUnencrypted.Should().BeTrue();
        await sut.SignInCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ConfirmAsync("SignIn_UnencryptedTitle", "Plain is unencrypted", "SignIn_UnencryptedAccept", "Dialog_Cancel");
        await _servers.DidNotReceiveWithAnyArgs().SignInAsync(default!, default!, default!, TestContext.Current.CancellationToken);
        _servers.DidNotReceiveWithAnyArgs().AcceptUnencrypted(default!);
        sut.Password.Should().Be("secret", "cancelling leaves the form as it was");
        sut.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task Accepting_the_warning_remembers_it_and_signs_in()
    {
        _dialogs.ConfirmAsync(default!, default!, default!, default!).ReturnsForAnyArgs(true);
        var sut = CreateSut("id-p");
        sut.Password = "secret";

        await sut.SignInCommand.ExecuteAsync(null);

        _servers.Received(1).AcceptUnencrypted("id-p");
        await _servers.Received(1).SignInAsync("id-p", "mike", "secret", Arg.Any<CancellationToken>());

        // Asked once: the next sign-in on this page goes ahead without the dialog.
        await sut.SignInCommand.ExecuteAsync(null);
        await _dialogs.ReceivedWithAnyArgs(1).ConfirmAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Local_addresses_are_warned_about_too()
    {
        Plain.Address.Host.Should().Be("192.168.1.10", "a private address on the home network");
        var sut = CreateSut("id-p");
        await sut.SignInCommand.ExecuteAsync(null);

        await _dialogs.ReceivedWithAnyArgs(1).ConfirmAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Https_and_accepted_servers_sign_in_without_asking()
    {
        var secure = CreateSut("id-s");
        secure.IsUnencrypted.Should().BeFalse();
        await secure.SignInCommand.ExecuteAsync(null);

        var accepted = CreateSut("id-a");
        accepted.IsUnencrypted.Should().BeTrue("the page still says so");
        await accepted.SignInCommand.ExecuteAsync(null);

        await _dialogs.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default!, default!, default!);
        await _servers.Received(1).SignInAsync("id-s", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _servers.Received(1).SignInAsync("id-a", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Quick_Connect_over_plain_http_asks_first()
    {
        _authentication.IsQuickConnectEnabledAsync(Plain.Address, Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut("id-p");
        await sut.CheckQuickConnectAsync(TestContext.Current.CancellationToken);

        await sut.QuickConnectCommand.ExecuteAsync(null);

        await _dialogs.ReceivedWithAnyArgs(1).ConfirmAsync(default!, default!, default!, default!);
        await _authentication.DidNotReceiveWithAnyArgs().StartQuickConnectAsync(default!, TestContext.Current.CancellationToken);
        sut.IsWaitingForQuickConnect.Should().BeFalse();
    }

    [Fact]
    public void ChooseServer_opens_the_server_page()
    {
        CreateSut().ChooseServerCommand.Execute(null);

        _navigation.Received(1).NavigateTo(PageKeys.Servers);
    }

    private async Task<SignInViewModel> CreateQuickConnectSutAsync()
    {
        _authentication.IsQuickConnectEnabledAsync(Alpha.Address, Arg.Any<CancellationToken>()).Returns(true);
        _authentication.StartQuickConnectAsync(Alpha.Address, Arg.Any<CancellationToken>()).Returns(new QuickConnectRequest("123456", "the-secret"));
        var sut = CreateSut();
        await sut.CheckQuickConnectAsync(TestContext.Current.CancellationToken);
        return sut;
    }

    /// <summary>Advances the fake clock by poll intervals until <paramref name="task"/> finishes.</summary>
    private async Task RunToCompletionAsync(Task task, TimeSpan step)
    {
        for (var i = 0; i < 500 && !task.IsCompleted; i++)
        {
            _time.Advance(step);
            await Task.WhenAny(task, Task.Delay(5, TestContext.Current.CancellationToken));
        }

        await task;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Quick_Connect_is_offered_only_when_the_server_enables_it(bool enabled)
    {
        _authentication.IsQuickConnectEnabledAsync(Alpha.Address, Arg.Any<CancellationToken>()).Returns(enabled);
        var sut = CreateSut();

        await sut.CheckQuickConnectAsync(TestContext.Current.CancellationToken);

        sut.IsQuickConnectAvailable.Should().Be(enabled);
        sut.ShowsQuickConnectButton.Should().Be(enabled);
        sut.QuickConnectCommand.CanExecute(null).Should().Be(enabled);
    }

    [Fact]
    public async Task Quick_Connect_check_failure_hides_the_option_without_an_error()
    {
        _authentication.IsQuickConnectEnabledAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new JellyfinException("offline")));
        var sut = CreateSut();

        await sut.CheckQuickConnectAsync(TestContext.Current.CancellationToken);

        sut.IsQuickConnectAvailable.Should().BeFalse();
        sut.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task Quick_Connect_shows_the_code_polls_until_approved_and_signs_in()
    {
        _authentication.IsQuickConnectApprovedAsync(Alpha.Address, "the-secret", Arg.Any<CancellationToken>())
            .Returns(false, false, true);
        var sut = await CreateQuickConnectSutAsync();
        string? shownCode = null;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(sut.QuickConnectCode) && sut.QuickConnectCode is { } code)
            {
                shownCode = code;
            }
        };

        var run = sut.QuickConnectCommand.ExecuteAsync(null);
        sut.IsWaitingForQuickConnect.Should().BeTrue();
        sut.ShowsQuickConnectButton.Should().BeFalse();
        await RunToCompletionAsync(run, SignInViewModel.QuickConnectPollInterval);

        shownCode.Should().Be("123456");
        await _authentication.Received(3).IsQuickConnectApprovedAsync(Alpha.Address, "the-secret", Arg.Any<CancellationToken>());
        await _servers.Received(1).SignInWithQuickConnectAsync("id-a", "the-secret", Arg.Any<CancellationToken>());
        _navigation.Received(1).NavigateTo(PageKeys.Home);
        sut.IsWaitingForQuickConnect.Should().BeFalse();
        sut.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task Quick_Connect_gives_up_after_the_timeout()
    {
        _authentication.IsQuickConnectApprovedAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var sut = await CreateQuickConnectSutAsync();

        var run = sut.QuickConnectCommand.ExecuteAsync(null);
        await RunToCompletionAsync(run, TimeSpan.FromSeconds(30));

        _time.GetUtcNow().Should().BeOnOrAfter(_time.Start + SignInViewModel.QuickConnectTimeout);
        sut.Error.Should().Be("SignIn_QuickConnectExpired");
        sut.IsWaitingForQuickConnect.Should().BeFalse();
        await _servers.DidNotReceiveWithAnyArgs().SignInWithQuickConnectAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cancelling_Quick_Connect_stops_polling_without_an_error()
    {
        _authentication.IsQuickConnectApprovedAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var sut = await CreateQuickConnectSutAsync();

        var run = sut.QuickConnectCommand.ExecuteAsync(null);
        sut.QuickConnectCancelCommand.Execute(null);
        await RunToCompletionAsync(run, SignInViewModel.QuickConnectPollInterval);

        sut.HasError.Should().BeFalse();
        sut.IsWaitingForQuickConnect.Should().BeFalse();
        await _servers.DidNotReceiveWithAnyArgs().SignInWithQuickConnectAsync(default!, default!, TestContext.Current.CancellationToken);
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!, default);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "SignIn_QuickConnectExpired")]
    [InlineData(HttpStatusCode.Unauthorized, "SignIn_QuickConnectFailed")]
    [InlineData(null, "SignIn_Unreachable")]
    public async Task Quick_Connect_failures_are_shown_on_the_page(HttpStatusCode? status, string expectedKey)
    {
        _authentication.IsQuickConnectApprovedAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new JellyfinException("x", status)));
        var sut = await CreateQuickConnectSutAsync();

        await sut.QuickConnectCommand.ExecuteAsync(null);

        sut.Error.Should().Be(expectedKey);
        sut.IsWaitingForQuickConnect.Should().BeFalse();
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!, default);
    }
}
