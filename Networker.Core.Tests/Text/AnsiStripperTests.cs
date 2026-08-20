using Networker.Core.Text;

namespace Networker.Core.Tests.Text;

public sealed class AnsiStripperTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("plain text", "plain text")]
    [InlineData("hello\r\nworld", "hello\r\nworld")]
    public void Strip_LeavesPlainText(string? input, string expected)
    {
        Assert.Equal(expected, AnsiStripper.Strip(input));
    }

    [Theory]
    [InlineData("\x1b[31mred\x1b[0m", "red")]
    [InlineData("\x1b[1;32mgreen\x1b[m", "green")]
    [InlineData("a\x1b[2Kb", "ab")]
    [InlineData("\x1b[?25lhide", "hide")]
    [InlineData("\x1b[39;49m", "")]
    [InlineData("\x1b[38;2;255;0;0mtruecolor", "truecolor")]
    [InlineData("\x1b[1A\x1b[2Kline", "line")]
    public void Strip_RemovesCsiSequences(string input, string expected)
    {
        Assert.Equal(expected, AnsiStripper.Strip(input));
    }

    [Theory]
    [InlineData("\x1b]0;Window Title\x07title", "title")]
    [InlineData("\x1b]2;path\x1b\\osctext", "osctext")]
    public void Strip_RemovesOscSequences(string input, string expected)
    {
        Assert.Equal(expected, AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_RemovesSingleAndTwoCharEscapes()
    {
        Assert.Equal("rest", AnsiStripper.Strip("\u001b7rest"));
        Assert.Equal("rest", AnsiStripper.Strip("\u001b8rest"));
        Assert.Equal("rest", AnsiStripper.Strip("\x1b(0rest"));
        Assert.Equal("rest", AnsiStripper.Strip("\x1b#8rest"));
    }

    [Fact]
    public void Strip_HandlesUnterminatedSequenceAtEnd()
    {
        Assert.Equal("start", AnsiStripper.Strip("start\x1b[31m"));
        Assert.Equal("start", AnsiStripper.Strip("start\x1b]0;unterminated"));
        Assert.Equal("start", AnsiStripper.Strip("start\x1b"));
    }

    [Fact]
    public void Strip_MixedEscapeAndUnicode()
    {
        string input = "\x1b[90mwarning:\x1b[0m \u25B6 path \x1b[1A\x1b[2K";
        Assert.Equal("warning: \u25B6 path ", AnsiStripper.Strip(input));
    }
}
