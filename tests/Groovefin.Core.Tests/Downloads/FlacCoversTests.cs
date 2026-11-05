using Groovefin.Core.Downloads;
using Groovefin.Core.Tests.TestDoubles;

namespace Groovefin.Core.Tests.Downloads;

public sealed class FlacCoversTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"groovefin-flac-{Guid.NewGuid():N}.flac");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void A_cover_becomes_padding_of_the_same_size_and_the_tags_and_audio_stay()
    {
        var original = FlacFile.WithCover();
        File.WriteAllBytes(_path, original);

        FlacCovers.Clear(_path).Should().Be(1);

        var changed = File.ReadAllBytes(_path);
        changed.Should().HaveCount(original.Length);
        var blocks = FlacFile.Blocks(changed);
        blocks.Select(b => b.Type).Should().Equal(FlacFile.StreamInfo, FlacFile.Tags, FlacFile.Padding);
        blocks[1].Data.Should().Equal(FlacFile.Blocks(original)[1].Data);
        blocks[2].Last.Should().BeTrue("the cover was the last block");
        blocks[2].Data.Should().HaveCount(100_000).And.OnlyContain(b => b == 0);
        changed[^FlacFile.Audio.Length..].Should().Equal(FlacFile.Audio);
    }

    [Fact]
    public void Every_cover_becomes_padding_and_the_blocks_after_it_stay()
    {
        var original = FlacFile.Build(
            (FlacFile.StreamInfo, new byte[34]),
            (FlacFile.Picture, [1, 2, 3]),
            (FlacFile.SeekTable, [4, 5, 6]),
            (FlacFile.Picture, [7, 8]));
        File.WriteAllBytes(_path, original);

        FlacCovers.Clear(_path).Should().Be(2);

        var blocks = FlacFile.Blocks(File.ReadAllBytes(_path));
        blocks.Select(b => (b.Type, b.Last)).Should().Equal(
            (FlacFile.StreamInfo, false), (FlacFile.Padding, false), (FlacFile.SeekTable, false), (FlacFile.Padding, true));
        blocks[2].Data.Should().Equal(4, 5, 6);
    }

    [Fact]
    public void Another_file_stays_as_it_is()
    {
        byte[] original = [0, 0, 0, 24, .. "ftypM4A "u8, 6, 0, 0, 0];
        File.WriteAllBytes(_path, original);

        FlacCovers.Clear(_path).Should().Be(0);

        File.ReadAllBytes(_path).Should().Equal(original);
    }

    [Fact]
    public void A_block_longer_than_the_file_stops_the_changes()
    {
        var original = FlacFile.WithCover()[..2_000];
        File.WriteAllBytes(_path, original);

        FlacCovers.Clear(_path).Should().Be(0);

        File.ReadAllBytes(_path).Should().Equal(original);
    }
}
