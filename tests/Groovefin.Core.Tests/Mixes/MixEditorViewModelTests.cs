using Groovefin.Core.Mixes;
using Groovefin.Core.Models;
using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.Mixes;

public sealed class MixEditorViewModelTests
{
    private static readonly Genre[] Genres = [new("g2", "Rock"), new("g1", "Ambient")];

    private readonly IStringResources _strings = Substitute.For<IStringResources>();

    public MixEditorViewModelTests() => _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());

    private MixEditorViewModel Sut(MixRules? existing = null) => new(_strings, Genres, existing);

    [Fact]
    public void A_new_mix_starts_with_every_song_random_and_fifty_songs()
    {
        var sut = Sut();

        sut.Title.Should().Be("MixEditor_NewTitle");
        sut.GenreOptions.Should().Equal("MixEditor_AnyGenre", "Ambient", "Rock");
        sut.CanSave.Should().BeFalse("it has no name yet");

        sut.Name = "  Everything  ";
        sut.Build().Should().Be(new MixRules { Name = "Everything" });
    }

    [Fact]
    public void The_choices_become_rules()
    {
        var sut = Sut();
        sut.Name = "Rock 90s";
        sut.GenreIndex = 2;
        sut.FromYear = 1990;
        sut.ToYear = 1999;
        sut.FavouritesOnly = true;
        sut.PlaysIndex = 3;
        sut.NotPlayedIndex = 3;
        sut.AddedIndex = 1;
        sut.OrderIndex = 1;
        sut.SizeIndex = 2;

        sut.Build().Should().Be(new MixRules
        {
            Name = "Rock 90s",
            GenreId = "g2",
            GenreName = "Rock",
            FromYear = 1990,
            ToYear = 1999,
            FavouritesOnly = true,
            MinPlays = 5,
            NotPlayedForDays = 182,
            AddedWithinDays = 7,
            Order = MixOrder.MostPlayed,
            Size = 100,
        });
    }

    [Fact]
    public void Saved_rules_come_back_as_the_same_choices()
    {
        var rules = new MixRules
        {
            Name = "Forgotten",
            GenreId = "g1",
            GenreName = "Ambient",
            ToYear = 2005,
            MaxPlays = 0,
            NotPlayedForDays = 365,
            Order = MixOrder.LeastRecentlyPlayed,
            Size = 25,
        };

        var sut = Sut(rules);

        sut.Title.Should().Be("MixEditor_EditTitle");
        sut.GenreIndex.Should().Be(1);
        double.IsNaN(sut.FromYear).Should().BeTrue();
        sut.PlaysIndex.Should().Be(1);
        sut.Build().Should().Be(rules);
    }

    [Fact]
    public void A_genre_the_server_no_longer_lists_is_kept()
    {
        var sut = Sut(new MixRules { Name = "Old", GenreId = "gone", GenreName = "Gone" });

        sut.GenreIndex.Should().Be(0);
        sut.Build().GenreId.Should().Be("gone");

        sut.GenreIndex = 2;
        sut.Build().GenreName.Should().Be("Rock");
    }

    [Theory]
    [InlineData(2000, 1990)]
    [InlineData(999, double.NaN)]
    [InlineData(1990.5, double.NaN)]
    public void Years_must_be_whole_and_in_order(double from, double to)
    {
        var sut = Sut();
        sut.Name = "Years";
        sut.FromYear = from;
        sut.ToYear = to;

        sut.CanSave.Should().BeFalse();
        sut.Invoking(s => s.Build()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Can_save_follows_the_name_and_years()
    {
        var sut = Sut();
        var changes = new List<string?>();
        sut.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        sut.Name = "x";
        sut.ToYear = 1980;

        changes.Should().Contain(nameof(MixEditorViewModel.CanSave));
        sut.CanSave.Should().BeTrue();
    }
}
