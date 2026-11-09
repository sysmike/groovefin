using Groovefin.Core.JumpLists;

namespace Groovefin.Core.Tests.JumpLists;

public sealed class JumpListCommandTests
{
    public static TheoryData<JumpListCommand> Commands =>
    [
        JumpListCommand.PlayPause,
        JumpListCommand.Next,
        JumpListCommand.ShuffleAll,
        JumpListCommand.ForPlaylist("0123456789abcdef0123456789abcdef"),
    ];

    [Theory]
    [MemberData(nameof(Commands))]
    public void Each_command_reads_back_from_its_arguments(JumpListCommand command)
    {
        JumpListCommand.Parse(command.Arguments).Should().Be(command);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Groovefin\\Groovefin.exe\" --next", JumpListAction.Next)]
    [InlineData("C:\\Groovefin\\Groovefin.exe --play-pause", JumpListAction.PlayPause)]
    [InlineData("  --shuffle-all  ", JumpListAction.ShuffleAll)]
    [InlineData("Groovefin.exe \"--play-playlist=abc-123\"", JumpListAction.PlayPlaylist)]
    public void A_command_line_with_the_programs_path_is_read(string commandLine, JumpListAction action)
    {
        JumpListCommand.Parse(commandLine)!.Action.Should().Be(action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\Groovefin\\Groovefin.exe")]
    [InlineData("--unknown")]
    [InlineData("--play-playlist=")]
    [InlineData("--play-playlist=a/b")]
    [InlineData("--play-playlist=a;rm")]
    public void Anything_else_is_no_command(string? commandLine)
    {
        JumpListCommand.Parse(commandLine).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("a\"b")]
    public void A_playlist_command_needs_a_plain_id(string id)
    {
        var create = () => JumpListCommand.ForPlaylist(id);

        create.Should().Throw<ArgumentException>();
    }
}
