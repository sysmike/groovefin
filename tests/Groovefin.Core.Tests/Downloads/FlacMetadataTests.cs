using Groovefin.Core.Downloads;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Downloads;

public sealed class FlacMetadataTests : IDisposable
{
    private const int LargeCover = (int)FlacMetadata.MaxAudioOffset;

    private readonly string _path = Path.Join(Path.GetTempPath(), $"groovefin-flac-{Guid.NewGuid():N}.flac");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Audio_behind_a_large_cover_moves_to_the_front_and_the_tags_stay()
    {
        var original = FlacFile.WithCover(LargeCover);
        File.WriteAllBytes(_path, original);

        var size = FlacMetadata.Compact(_path);

        var changed = File.ReadAllBytes(_path);
        size.Should().Be(changed.Length).And.BeLessThan(original.Length - LargeCover + 100);
        var blocks = FlacFile.Blocks(changed);
        blocks.Select(b => (b.Type, b.Last)).Should().Equal((FlacFile.StreamInfo, false), (FlacFile.Tags, true));
        blocks[1].Data.Should().Equal(FlacFile.Blocks(original)[1].Data);
        changed[^FlacFile.Audio.Length..].Should().Equal(FlacFile.Audio);
        File.Exists(_path + ".flac-tmp").Should().BeFalse();
    }

    [Fact]
    public void Padding_and_every_picture_go_and_the_other_blocks_keep_their_order()
    {
        File.WriteAllBytes(_path, FlacFile.Build(
            (FlacFile.StreamInfo, new byte[34]),
            (FlacFile.Picture, [1, 2, 3]),
            (FlacFile.SeekTable, [4, 5, 6]),
            (FlacFile.Padding, new byte[LargeCover]),
            (FlacFile.Tags, [7, 8])));

        FlacMetadata.Compact(_path).Should().NotBeNull();

        var blocks = FlacFile.Blocks(File.ReadAllBytes(_path));
        blocks.Select(b => (b.Type, b.Last)).Should().Equal((FlacFile.StreamInfo, false), (FlacFile.SeekTable, false), (FlacFile.Tags, true));
        blocks[1].Data.Should().Equal(4, 5, 6);
        blocks[2].Data.Should().Equal(7, 8);
    }

    [Fact]
    public void A_file_whose_audio_starts_soon_keeps_its_cover()
    {
        var original = FlacFile.WithCover(100_000);
        File.WriteAllBytes(_path, original);

        FlacMetadata.Compact(_path).Should().BeNull();

        File.ReadAllBytes(_path).Should().Equal(original);
    }

    [Fact]
    public void Another_file_stays_as_it_is()
    {
        byte[] original = [0, 0, 0, 24, .. "ftypM4A "u8, 6, 0, 0, 0];
        File.WriteAllBytes(_path, original);

        FlacMetadata.Compact(_path).Should().BeNull();

        File.ReadAllBytes(_path).Should().Equal(original);
    }

    [Fact]
    public void A_block_longer_than_the_file_leaves_it_as_it_is()
    {
        var original = FlacFile.WithCover(LargeCover)[..2_000];
        File.WriteAllBytes(_path, original);

        FlacMetadata.Compact(_path).Should().BeNull();

        File.ReadAllBytes(_path).Should().Equal(original);
    }
}
