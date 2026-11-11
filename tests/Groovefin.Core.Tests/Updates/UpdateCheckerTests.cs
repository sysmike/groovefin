using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.Updates;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;

namespace Groovefin.Core.Tests.Updates;

public sealed class UpdateCheckerTests : IDisposable
{
    private static readonly ReleaseInfo Newer = new("1.0.0", new Uri("https://github.com/sysmike/groovefin/releases/tag/v1.0.0"));

    private readonly IReleaseSource _source = Substitute.For<IReleaseSource>();
    private readonly InMemorySettingsService _settings = new();
    private readonly FakeNetworkStatus _network = new();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly FakeTimeProvider _time = new();
    private readonly UpdateChecker _sut;

    public UpdateCheckerTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Updates_NotificationTitle").Returns("Groovefin {0} is available");
        _strings.GetString("Updates_Available").Returns("Groovefin {0} is available.");
        _source.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(Newer);
        _sut = Create("0.9.0-beta1");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _sut.Dispose();

    private UpdateChecker Create(string installed) => new(
        _source,
        new AppVersion(installed, null),
        _settings,
        _network,
        _notifications,
        _strings,
        new ImmediateDispatcher(),
        _time,
        NullLogger<UpdateChecker>.Instance);

    private int Asked => _source.ReceivedCalls().Count();

    private async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(10, Ct);
        }
    }

    [Fact]
    public async Task A_newer_release_is_announced_once()
    {
        (await _sut.CheckAsync(cancellationToken: Ct)).Should().Be(UpdateCheckResult.Available);
        (await _sut.CheckAsync(cancellationToken: Ct)).Should().Be(UpdateCheckResult.Available);

        _sut.Available.Should().Be(Newer);
        _notifications.Received(1).Show(new AppNotification(NotificationSeverity.Informational, "Groovefin 1.0.0 is available", "Updates_NotificationMessage"));
        _settings.Current.Updates.NotifiedVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task A_release_announced_before_this_start_is_not_announced_again_but_still_shown_in_settings()
    {
        _settings.Update(s => s with { Updates = s.Updates with { NotifiedVersion = "1.0.0" } });

        await _sut.CheckAsync(cancellationToken: Ct);

        _notifications.DidNotReceiveWithAnyArgs().Show(default!);
        _sut.Available.Should().Be(Newer);
    }

    [Fact]
    public async Task The_installed_release_is_up_to_date()
    {
        using var sut = Create("1.0.0");

        (await sut.CheckAsync(cancellationToken: Ct)).Should().Be(UpdateCheckResult.UpToDate);

        sut.Available.Should().BeNull();
        sut.HasChecked.Should().BeTrue();
        _notifications.DidNotReceiveWithAnyArgs().Show(default!);
    }

    [Fact]
    public async Task Without_a_final_release_nothing_is_announced()
    {
        _source.GetLatestAsync(Arg.Any<CancellationToken>()).Returns((ReleaseInfo?)null);

        (await _sut.CheckAsync(cancellationToken: Ct)).Should().Be(UpdateCheckResult.UpToDate);
    }

    [Fact]
    public async Task A_check_that_fails_tells_nothing_and_the_next_one_asks_again()
    {
        _source.GetLatestAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("No route to host."));

        (await _sut.CheckAsync(cancellationToken: Ct)).Should().Be(UpdateCheckResult.Failed);

        _sut.HasChecked.Should().BeFalse();
        _notifications.DidNotReceiveWithAnyArgs().Show(default!);
        _source.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(Newer);
        (await _sut.CheckAsync(cancellationToken: Ct)).Should().Be(UpdateCheckResult.Available);
    }

    [Fact]
    public async Task The_timer_checks_after_start_up_and_then_once_a_day()
    {
        _time.Advance(UpdateChecker.FirstCheckDelay);
        await UntilAsync(() => _sut.HasChecked);
        Asked.Should().Be(1);

        _time.Advance(UpdateChecker.CheckInterval);
        await UntilAsync(() => Asked == 2);

        Asked.Should().Be(2);
    }

    [Fact]
    public async Task Turned_off_or_without_a_network_only_the_users_own_check_asks()
    {
        _settings.Update(s => s with { Updates = s.Updates with { CheckAutomatically = false } });
        (await _sut.CheckAsync(manual: false, Ct)).Should().Be(UpdateCheckResult.Skipped);
        _settings.Update(s => s with { Updates = s.Updates with { CheckAutomatically = true } });
        _network.IsOnline = false;
        (await _sut.CheckAsync(manual: false, Ct)).Should().Be(UpdateCheckResult.Skipped);

        Asked.Should().Be(0);
        (await _sut.CheckAsync(manual: true, Ct)).Should().Be(UpdateCheckResult.Available);
    }

    [Fact]
    public void The_check_is_on_by_default() => new AppSettings().Updates.CheckAutomatically.Should().BeTrue();

    [Fact]
    public async Task The_settings_show_what_the_check_found()
    {
        using var viewModel = new UpdatesViewModel(_sut, _settings, _strings, new ImmediateDispatcher());
        viewModel.StatusText.Should().Be("Updates_NotChecked");
        viewModel.HasUpdate.Should().BeFalse();

        await viewModel.CheckNowCommand.ExecuteAsync(null);

        viewModel.StatusText.Should().Be("Groovefin 1.0.0 is available.");
        viewModel.HasUpdate.Should().BeTrue();
        viewModel.ReleaseUri.Should().Be(Newer.Page);
        viewModel.IsChecking.Should().BeFalse();
    }

    [Fact]
    public async Task The_settings_say_when_the_check_failed_and_when_nothing_is_newer()
    {
        using var viewModel = new UpdatesViewModel(_sut, _settings, _strings, new ImmediateDispatcher());
        _source.GetLatestAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("No route to host."));
        await viewModel.CheckNowCommand.ExecuteAsync(null);
        viewModel.StatusText.Should().Be("Updates_Failed");

        _source.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(new ReleaseInfo("0.8.0", Newer.Page));
        await viewModel.CheckNowCommand.ExecuteAsync(null);

        viewModel.StatusText.Should().Be("Updates_UpToDate");
    }

    [Fact]
    public void The_switch_changes_the_setting()
    {
        using var viewModel = new UpdatesViewModel(_sut, _settings, _strings, new ImmediateDispatcher());

        viewModel.CheckAutomatically = false;

        _settings.Current.Updates.CheckAutomatically.Should().BeFalse();
        viewModel.CheckAutomatically.Should().BeFalse();
    }
}
