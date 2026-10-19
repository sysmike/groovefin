using System.Net;
using Groovefin.Core.Api;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Groovefin.Core.Tests.Notifications;

public sealed class ErrorReporterTests
{
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly FakeLogger<ErrorReporter> _logger = new();
    private readonly ErrorReporter _sut;

    public ErrorReporterTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Error_Server").Returns("Server error (HTTP {0}).");
        _sut = new ErrorReporter(_notifications, _strings, _logger);
    }

    public static TheoryData<Exception, string> Classifications => new()
    {
        { new JellyfinException("x", HttpStatusCode.Unauthorized), "Error_Unauthorized" },
        { new JellyfinException("x", statusCode: null), "Error_Unreachable" },
        { new IOException("disk full"), "Error_Storage" },
        { new UnauthorizedAccessException("denied"), "Error_Storage" },
        { new InvalidOperationException("bug"), "Error_Unexpected" },
    };

    [Theory]
    [MemberData(nameof(Classifications))]
    public void Exception_is_shown_with_a_matching_message(Exception exception, string expectedMessage)
    {
        _sut.Report(exception, Operations.LoadAlbums);

        _notifications.Received(1).Show(new AppNotification(NotificationSeverity.Error, Operations.LoadAlbums, expectedMessage));
    }

    [Fact]
    public void Server_error_message_contains_the_status_code()
    {
        _sut.Report(new JellyfinException("x", HttpStatusCode.InternalServerError), Operations.LoadAlbums);

        _notifications.Received(1).Show(Arg.Is<AppNotification>(n => n.Message == "Server error (HTTP 500)."));
    }

    [Fact]
    public void Cancellation_is_not_reported()
    {
        _sut.Report(new OperationCanceledException(), Operations.LoadAlbums);
        _sut.Report(new TaskCanceledException(), Operations.LoadAlbums);

        _notifications.DidNotReceiveWithAnyArgs().Show(default!);
        _logger.Collector.Count.Should().Be(0);
    }

    [Fact]
    public void Expected_failures_are_logged_as_warnings_and_bugs_as_errors()
    {
        _sut.Report(new JellyfinException("x", statusCode: null), Operations.LoadAlbums);
        _sut.Report(new InvalidOperationException("bug"), Operations.LoadAlbums);

        _logger.Collector.GetSnapshot().Select(r => r.Level).Should().Equal(LogLevel.Warning, LogLevel.Error);
        _logger.Collector.GetSnapshot().Should().OnlyContain(r => r.Exception != null);
    }
}
