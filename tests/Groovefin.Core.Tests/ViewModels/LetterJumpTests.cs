using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class LetterJumpTests
{
    [Fact]
    public void The_letters_are_the_hash_and_a_to_z()
    {
        LetterJump.Letters.Should().HaveCount(27);
        LetterJump.Letters[0].Should().Be("#");
        LetterJump.Letters[^1].Should().Be("Z");
        LetterJump.NameBefore("Q").Should().Be("q");
        LetterJump.NameBefore("#").Should().BeNull();
    }

    [Fact]
    public async Task Loading_stops_when_nothing_more_comes()
    {
        var loaded = 10;
        var calls = 0;

        var index = await LetterJump.LoadUpToAsync("X", _ => Task.FromResult(500), () => loaded, () => 1000, _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        calls.Should().Be(1, "a load that brought nothing is not repeated");
        index.Should().Be(9, "the last loaded item is the nearest");
    }

    [Fact]
    public async Task Far_letters_load_in_large_pages_and_an_empty_list_has_no_index()
    {
        var loaded = 0;
        var asked = new List<int>();

        var index = await LetterJump.LoadUpToAsync("Y", _ => Task.FromResult(2500), () => loaded, () => 3000, count =>
        {
            asked.Add(count);
            loaded += count;
            return Task.CompletedTask;
        });

        asked.Should().Equal(LetterJump.MaxPage, LetterJump.MaxPage, 501);
        index.Should().Be(2500);
        (await LetterJump.LoadUpToAsync("A", _ => Task.FromResult(0), () => 0, () => 0, _ => Task.CompletedTask)).Should().BeNull();
    }
}
