using Groovefin.Core.Api;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class GenresViewModelTests
{
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly GenresViewModel _sut;

    public GenresViewModelTests() => _sut = new GenresViewModel(_library, _navigation, _errors);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Load_lists_the_genres_and_a_click_opens_one()
    {
        _library.GetAlbumGenresAsync(Arg.Any<CancellationToken>()).Returns([new Genre("g1", "Electronic"), new Genre("g2", "Jazz")]);

        await _sut.LoadAsync(Ct);
        _sut.OpenCommand.Execute(_sut.Genres[1]);

        _sut.Genres.Select(g => g.Name).Should().Equal("Electronic", "Jazz");
        _sut.IsEmpty.Should().BeFalse();
        _navigation.Received(1).NavigateTo(PageKeys.Genre, "g2");
    }

    [Fact]
    public async Task No_genres_shows_the_empty_state()
    {
        _library.GetAlbumGenresAsync(Arg.Any<CancellationToken>()).Returns([]);

        await _sut.LoadAsync(Ct);

        _sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_load_is_reported_and_is_not_empty()
    {
        _library.GetAlbumGenresAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Genre>>(_ => throw new JellyfinException("boom"));

        await _sut.LoadAsync(Ct);

        _sut.LoadFailed.Should().BeTrue();
        _sut.IsEmpty.Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadGenres);
    }

    [Fact]
    public async Task Cancelled_loads_are_quiet()
    {
        _library.GetAlbumGenresAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Genre>>(_ => throw new OperationCanceledException());

        await _sut.LoadAsync(Ct);

        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }
}
