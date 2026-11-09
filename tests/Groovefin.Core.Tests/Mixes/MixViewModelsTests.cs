using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Mixes;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace Groovefin.Core.Tests.Mixes;

public sealed class MixViewModelsTests : IDisposable
{
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IPlaylistService _playlists = Substitute.For<IPlaylistService>();
    private readonly IPlayer _player = Substitute.For<IPlayer>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly IMixEditor _editor = Substitute.For<IMixEditor>();
    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakePlaybackEngine _engine = new();
    private readonly MixStore _store;
    private readonly MixBuilder _builder;
    private readonly MixEditing _editing;

    public MixViewModelsTests()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _strings.GetString("Mixes_PlayName").Returns("Play {0}");
        _strings.GetString("OnRepeat_Plays").Returns("{0} plays");
        _servers.IsSignedIn.Returns(true);
        _servers.ActiveServer.Returns(new ServerProfile { Id = "s1", Name = "s1", Address = new Uri("https://music.example"), UserId = "u1" });
        _library.GetAlbumGenresAsync(Arg.Any<CancellationToken>()).Returns([new Genre("g1", "Rock")]);
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Track>([Song("a", 3), Song("b", 1)], 2, 0));
        _store = new MixStore(new InMemorySettingsService(), _servers);
        _builder = new MixBuilder(_library, new FakeTimeProvider());
        _editing = new MixEditing(_editor, _library, _strings, _store);
    }

    public void Dispose() => _store.Dispose();

    private static Track Song(string id, int plays) => new() { Id = id, Name = id, PlayCount = plays };

    private static Mix Saved(string id = "m1", string name = "Rock") => new() { Id = id, Rules = new MixRules { Name = name } };

    /// <summary>The editor "saves" with <paramref name="name"/> typed in.</summary>
    private void EditorSaves(string name, int genreIndex = 0) =>
        _editor.ShowAsync(Arg.Any<MixEditorViewModel>()).Returns(call =>
        {
            var editor = call.Arg<MixEditorViewModel>();
            editor.Name = name;
            editor.GenreIndex = genreIndex;
            return true;
        });

    private MixesViewModel Mixes() => new(_store, _editing, _builder, _player, _navigation, _dialogs, _errors, _strings, new ImmediateDispatcher());

    private MixViewModel Mix() => new(
        _store, _editing, _builder, _playlists, _player, _navigation, _dialogs, _errors, _strings,
        new FavouritesService(_library, _messenger, _errors), _messenger, new CurrentTrack(_engine));

    [Fact]
    public async Task A_new_mix_is_saved_with_the_editors_choices_and_opens()
    {
        EditorSaves("Rock", genreIndex: 1);
        using var sut = Mixes();
        sut.IsEmpty.Should().BeTrue();

        await sut.NewCommand.ExecuteAsync(null);

        var mix = _store.Mixes.Should().ContainSingle().Subject;
        mix.Rules.GenreName.Should().Be("Rock");
        _navigation.Received(1).NavigateTo(PageKeys.Mix, mix.Id);
        sut.Mixes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "Rock", PlayName = "Play Rock" });
        sut.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task A_cancelled_editor_saves_nothing()
    {
        _editor.ShowAsync(Arg.Any<MixEditorViewModel>()).Returns(false);
        using var sut = Mixes();

        await sut.NewCommand.ExecuteAsync(null);

        _store.Mixes.Should().BeEmpty();
        _navigation.DidNotReceiveWithAnyArgs().NavigateTo(default!, default);
    }

    [Fact]
    public async Task Without_the_servers_genres_the_editor_still_opens()
    {
        _library.GetAlbumGenresAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<Genre>>(new JellyfinException("offline")));
        EditorSaves("Any");

        (await _editing.EditAsync(null))!.Rules.GenreId.Should().BeNull();
    }

    [Fact]
    public async Task A_row_plays_and_deletes_its_mix_after_asking()
    {
        _store.Save(Saved());
        using var sut = Mixes();
        var row = sut.Mixes[0];

        await sut.PlayCommand.ExecuteAsync(row);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false, true);
        await sut.DeleteCommand.ExecuteAsync(row);
        _store.Mixes.Should().ContainSingle();
        await sut.DeleteCommand.ExecuteAsync(row);

        _player.Received(1).Play(Arg.Is<IReadOnlyList<Track>>(t => t.Count == 2));
        _store.Mixes.Should().BeEmpty();
        sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task A_mix_page_shows_the_rules_and_the_songs_with_their_plays()
    {
        _store.Save(Saved());
        var sut = Mix();

        await sut.LoadAsync("m1", TestContext.Current.CancellationToken);

        sut.Name.Should().Be("Rock");
        sut.Summary.Should().Be("MixOrder_Random · Mix_Size");
        sut.Entries.Select(e => (e.Track.Id, e.Plays, e.Rank)).Should().Equal(("a", 3, 1), ("b", 1, 2));
        sut.HasEntries.Should().BeTrue();
        sut.PlayCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task A_missing_mix_says_so()
    {
        var sut = Mix();

        await sut.LoadAsync("gone", TestContext.Current.CancellationToken);

        sut.IsMissing.Should().BeTrue();
        sut.EditCommand.CanExecute(null).Should().BeFalse();
        await _library.DidNotReceiveWithAnyArgs().GetTracksAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Saving_as_a_playlist_names_it_after_the_mix_and_opens_it()
    {
        _store.Save(Saved());
        _playlists.CreateAsync("Rock", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns("p1");
        var sut = Mix();
        await sut.LoadAsync("m1", TestContext.Current.CancellationToken);

        await sut.SaveAsPlaylistCommand.ExecuteAsync(null);

        await _playlists.Received(1).CreateAsync("Rock", Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "a", "b" })), Arg.Any<CancellationToken>());
        _navigation.Received(1).NavigateTo(PageKeys.Playlist, "p1");
    }

    [Fact]
    public async Task Editing_on_the_mix_page_saves_and_picks_again()
    {
        _store.Save(Saved());
        EditorSaves("Rock again");
        var sut = Mix();
        await sut.LoadAsync("m1", TestContext.Current.CancellationToken);

        await sut.EditCommand.ExecuteAsync(null);

        sut.Name.Should().Be("Rock again");
        _store.Mixes.Should().ContainSingle().Which.Id.Should().Be("m1");
        await _library.Received(2).GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deleting_on_the_mix_page_goes_to_the_mixes()
    {
        _store.Save(Saved());
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var sut = Mix();
        await sut.LoadAsync("m1", TestContext.Current.CancellationToken);

        await sut.DeleteCommand.ExecuteAsync(null);

        _store.Mixes.Should().BeEmpty();
        _navigation.Received(1).NavigateTo(PageKeys.Mixes);
    }

    [Fact]
    public async Task A_failed_pick_is_shown()
    {
        _store.Save(Saved());
        _library.GetTracksAsync(Arg.Any<TrackQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PagedResult<Track>>(new JellyfinException("down")));
        var sut = Mix();

        await sut.LoadAsync("m1", TestContext.Current.CancellationToken);

        sut.LoadFailed.Should().BeTrue();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadMix);
    }
}
