using Groovefin.Core.Api;
using Groovefin.Core.History;
using Groovefin.Core.Playback;
using Groovefin.Core.Privacy;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class ListeningHistoryViewModelTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly MemoryPlayHistoryStore _store = new();
    private readonly PlaybackReporting _reporting;
    private readonly PlayHistory _history;
    private readonly ListeningHistoryViewModel _sut;

    public ListeningHistoryViewModelTests()
    {
        var engine = new FakePlaybackEngine(time: _time);
        var client = Substitute.For<IJellyfinClient>();
        client.Connection.Returns(new ServerConnection(new Uri("https://jellyfin.example/"), "alice", "token"));
        var privacy = new PrivacyMode();
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _reporting = new PlaybackReporting(engine, Substitute.For<IPlaybackReporter>(), privacy, new ImmediateDispatcher(), _time, NullLogger<PlaybackReporting>.Instance);
        _history = new PlayHistory(engine, _reporting, client, Substitute.For<ILibraryService>(), _store, privacy, _time, NullLogger<PlayHistory>.Instance);
        _sut = new ListeningHistoryViewModel(_history, _dialogs, _strings);

        var data = new PlayHistoryData();
        data.Plays.Add(new PlayRecord("a", _time.GetUtcNow().AddDays(-1), PlaySource.Groovefin));
        _store.Preload(data);
    }

    public void Dispose()
    {
        _history.Dispose();
        _reporting.Dispose();
    }

    [Fact]
    public async Task Clear_asks_first_and_cancel_keeps_the_plays()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await _sut.ClearCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ConfirmAsync("History_ClearTitle", "History_ClearMessage", "History_ClearConfirm", "Dialog_Cancel");
        (await _history.GetLogAsync()).Plays.Should().ContainSingle();
    }

    [Fact]
    public async Task Clear_once_confirmed_drops_the_plays()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        await _sut.ClearCommand.ExecuteAsync(null);

        (await _history.GetLogAsync()).Plays.Should().BeEmpty();
    }
}
