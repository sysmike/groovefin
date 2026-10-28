using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Notifications;

public sealed class NotificationServiceTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly NotificationService _sut;

    public NotificationServiceTests() => _sut = new NotificationService(new ImmediateDispatcher(), _time);

    private static AppNotification Error(string title) => new(NotificationSeverity.Error, title, "message");

    private static AppNotification Success(string title) => new(NotificationSeverity.Success, title, "message");

    [Fact]
    public void Show_adds_the_notification()
    {
        _sut.Show(Error("a"));

        _sut.Notifications.Should().ContainSingle().Which.Title.Should().Be("a");
    }

    [Fact]
    public void Equal_notifications_are_shown_once()
    {
        _sut.Show(Error("a"));
        _sut.Show(Error("a"));

        _sut.Notifications.Should().ContainSingle();
    }

    [Fact]
    public void Oldest_notifications_are_dropped_beyond_the_limit()
    {
        foreach (var title in new[] { "1", "2", "3", "4", "5" })
        {
            _sut.Show(Error(title));
        }

        _sut.Notifications.Select(n => n.Title).Should().Equal("3", "4", "5");
    }

    [Fact]
    public void Dismiss_removes_the_notification()
    {
        var notification = Error("a");
        _sut.Show(notification);

        _sut.Dismiss(notification);

        _sut.Notifications.Should().BeEmpty();
    }

    [Fact]
    public void Changes_are_made_through_the_dispatcher()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.EnqueueAsync(Arg.Any<Action>()).Returns(Task.CompletedTask);
        var sut = new NotificationService(dispatcher);

        sut.Show(Error("a"));

        dispatcher.Received(1).EnqueueAsync(Arg.Any<Action>());
        sut.Notifications.Should().BeEmpty();
    }

    [Theory]
    [InlineData(NotificationSeverity.Success)]
    [InlineData(NotificationSeverity.Informational)]
    public void A_confirmation_closes_by_itself(NotificationSeverity severity)
    {
        _sut.Show(new AppNotification(severity, "Added to playlist", "1 song was added to Chill."));

        _time.Advance(NotificationService.AutoCloseDelay - TimeSpan.FromMilliseconds(1));
        _sut.Notifications.Should().ContainSingle();

        _time.Advance(TimeSpan.FromMilliseconds(1));
        _sut.Notifications.Should().BeEmpty();
    }

    [Theory]
    [InlineData(NotificationSeverity.Error)]
    [InlineData(NotificationSeverity.Warning)]
    public void Errors_and_warnings_stay_until_closed(NotificationSeverity severity)
    {
        _sut.Show(new AppNotification(severity, "Playback failed", "message"));

        _time.Advance(TimeSpan.FromMinutes(10));

        _sut.Notifications.Should().ContainSingle();
    }

    [Fact]
    public void A_confirmation_shown_again_stays_its_full_time_from_then()
    {
        _sut.Show(Success("Added"));
        _time.Advance(TimeSpan.FromSeconds(4));

        _sut.Show(Success("Added"));
        _time.Advance(TimeSpan.FromSeconds(4));

        _sut.Notifications.Should().ContainSingle();
        _time.Advance(TimeSpan.FromSeconds(1));
        _sut.Notifications.Should().BeEmpty();
    }

    [Fact]
    public void A_confirmation_closed_early_does_not_take_a_later_one_with_it()
    {
        var first = Success("Added");
        _sut.Show(first);
        _sut.Dismiss(first);
        _time.Advance(TimeSpan.FromSeconds(3));

        // The same text again: the first timer must not close it two seconds in.
        _sut.Show(Success("Added"));
        _time.Advance(TimeSpan.FromSeconds(2));

        _sut.Notifications.Should().ContainSingle();
    }

    [Fact]
    public void A_confirmation_pushed_out_by_newer_ones_stops_its_timer()
    {
        _sut.Show(Success("old"));
        _sut.Show(Error("1"));
        _sut.Show(Error("2"));
        _sut.Show(Error("3"));
        _sut.Show(Success("old"));

        _time.Advance(TimeSpan.FromSeconds(4));

        _sut.Notifications.Select(n => n.Title).Should().Equal("2", "3", "old");
    }
}
