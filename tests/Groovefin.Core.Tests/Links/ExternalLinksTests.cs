using Groovefin.Core.Links;
using Groovefin.Core.Models;

namespace Groovefin.Core.Tests.Links;

public sealed class ExternalLinksTests
{
    private static Album Album(string name = "Random Access Memories", string? artist = "Daft Punk") =>
        new() { Id = "al1", Name = name, AlbumArtist = artist };

    [Fact]
    public void MusicBrainz_prefers_the_release_group()
    {
        var album = Album() with { MusicBrainzReleaseGroupId = "aa997ea0", MusicBrainzReleaseId = "bb12" };

        ExternalLinks.MusicBrainz(album)!.AbsoluteUri.Should().Be("https://musicbrainz.org/release-group/aa997ea0");
    }

    [Fact]
    public void MusicBrainz_falls_back_to_the_release()
    {
        var album = Album() with { MusicBrainzReleaseId = "bb12" };

        ExternalLinks.MusicBrainz(album)!.AbsoluteUri.Should().Be("https://musicbrainz.org/release/bb12");
    }

    [Fact]
    public void Without_a_MusicBrainz_id_there_is_no_link() =>
        ExternalLinks.MusicBrainz(Album()).Should().BeNull();

    [Theory]
    [InlineData("Daft Punk", "Random Access Memories", "https://www.last.fm/music/Daft+Punk/Random+Access+Memories")]
    [InlineData("AC/DC", "Back in Black", "https://www.last.fm/music/AC%2FDC/Back+in+Black")]
    [InlineData("Sigur Rós", "( )", "https://www.last.fm/music/Sigur+R%C3%B3s/%28+%29")]
    public void LastFm_links_artist_and_album(string artist, string name, string expected) =>
        ExternalLinks.LastFm(Album(name, artist))!.AbsoluteUri.Should().Be(expected);

    [Fact]
    public void Without_an_artist_there_is_no_Last_fm_link() =>
        ExternalLinks.LastFm(Album(artist: null)).Should().BeNull();
}
