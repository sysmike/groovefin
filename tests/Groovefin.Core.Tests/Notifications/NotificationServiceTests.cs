using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Notifications;

public sealed class NotificationServiceTests
{
    private readonly NotificationService _sut = new(new ImmediateDispatcher());

    private static AppNotification Error(string title) => new(NotificationSeverity.Error, title, "message");

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
}
