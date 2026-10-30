using CommunityToolkit.Mvvm.Messaging;
using Groovefin.Core.Api;
using Groovefin.Core.Favourites;
using Groovefin.Core.Notifications;

namespace Groovefin.Core.Tests.Favourites;

public sealed class FavouritesServiceTests
{
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly List<FavouriteChangedMessage> _sent = [];
    private readonly FavouritesService _sut;

    public FavouritesServiceTests()
    {
        _messenger.Register<FavouritesServiceTests, FavouriteChangedMessage>(this, static (r, m) => r._sent.Add(m));
        _sut = new FavouritesService(_library, _messenger, _errors);
    }

    [Fact]
    public async Task A_saved_change_is_sent_to_every_view_and_remembered()
    {
        (await _sut.SetAsync("t1", true)).Should().BeTrue();

        await _library.Received(1).SetFavouriteAsync("t1", true, Arg.Any<CancellationToken>());
        _sent.Should().Equal(new FavouriteChangedMessage("t1", true));
        _sut.IsFavourite("t1", loaded: false).Should().BeTrue();
    }

    [Fact]
    public async Task A_refused_change_is_reported_and_not_sent()
    {
        var failure = new JellyfinException("no");
        _library.SetFavouriteAsync("t1", true, Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));

        (await _sut.SetAsync("t1", true)).Should().BeFalse();

        _sent.Should().BeEmpty();
        _errors.Received(1).Report(failure, Operations.SetFavourite);
        _sut.IsFavourite("t1", loaded: false).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Items_not_changed_in_this_session_keep_their_loaded_state(bool loaded)
    {
        _sut.IsFavourite("t1", loaded).Should().Be(loaded);
    }

    [Fact]
    public async Task Several_items_are_saved_one_after_the_other()
    {
        (await _sut.SetAllAsync(["t1", "t2"], true)).Should().BeTrue();

        _sent.Should().Equal(new FavouriteChangedMessage("t1", true), new FavouriteChangedMessage("t2", true));
    }

    [Fact]
    public async Task Several_items_stop_at_the_first_refusal_with_one_error()
    {
        var failure = new JellyfinException("offline");
        _library.SetFavouriteAsync("t2", true, Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));

        (await _sut.SetAllAsync(["t1", "t2", "t3"], true)).Should().BeFalse();

        _sent.Should().Equal(new FavouriteChangedMessage("t1", true));
        await _library.DidNotReceive().SetFavouriteAsync("t3", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        _errors.Received(1).Report(failure, Operations.SetFavourite);
    }

    [Fact]
    public async Task An_empty_id_is_rejected()
    {
        var act = () => _sut.SetAsync(string.Empty, true);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
