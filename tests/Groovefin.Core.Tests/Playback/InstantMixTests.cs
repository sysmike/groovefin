using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Playback;

public sealed class InstantMixTests
{
    private static readonly Track[] Mix = [new() { Id = "m1", Name = "One" }, new() { Id = "m2", Name = "Two" }];

    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly InMemorySettingsService _settings = new();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly InstantMix _sut;

    public InstantMixTests()
    {
        _library.GetInstantMixAsync("x", InstantMix.Size, Arg.Any<CancellationToken>()).Returns(Mix);
        _sut = new InstantMix(_library, _player, _settings, _errors);
    }

    [Fact]
    public async Task By_default_the_mix_replaces_the_queue()
    {
        await _sut.StartAsync("x");

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(Mix)));
        _player.DidNotReceiveWithAnyArgs().AddToQueue(default!);
    }

    [Fact]
    public async Task With_append_the_mix_goes_to_the_end_of_the_queue()
    {
        _settings.Update(s => s with { Playback = s.Playback with { InstantMix = InstantMixMode.Append } });

        await _sut.StartAsync("x");

        _player.Received(1).AddToQueue(Arg.Is<IReadOnlyList<Track>>(t => t.SequenceEqual(Mix)));
        _player.DidNotReceiveWithAnyArgs().Play(default!);
    }

    [Fact]
    public async Task An_empty_mix_does_nothing()
    {
        _library.GetInstantMixAsync("x", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await _sut.StartAsync("x");

        _player.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_mix_is_reported()
    {
        _library.GetInstantMixAsync("x", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Track>>(_ => throw new JellyfinException("boom"));

        await _sut.StartAsync("x");

        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.Playback);
    }

    [Fact]
    public async Task An_empty_id_is_rejected()
    {
        var act = () => _sut.StartAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
