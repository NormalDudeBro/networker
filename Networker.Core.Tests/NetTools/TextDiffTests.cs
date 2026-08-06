using Networker.Core.NetTools.Config;

namespace Networker.Core.Tests.NetTools;

public class TextDiffTests
{
    [Fact]
    public void DiffLines_IdenticalInputs_HasOnlyEqualLines()
    {
        var text = "line1\nline2\nline3";
        var diff = TextDiff.DiffLines(text, text);
        Assert.All(diff, l => Assert.Equal(DiffLineKind.Equal, l.Kind));
        Assert.Equal(3, diff.Count);
    }

    [Fact]
    public void DiffLines_DetectsAddedAndRemoved()
    {
        var oldText = "a\nb\nc";
        var newText = "a\nX\nc";

        var diff = TextDiff.DiffLines(oldText, newText);

        Assert.Contains(diff, l => l.Kind == DiffLineKind.Added && l.Text == "X");
        Assert.Contains(diff, l => l.Kind == DiffLineKind.Removed && l.Text == "b");
        Assert.Contains(diff, l => l.Kind == DiffLineKind.Equal && l.Text == "a");
    }

    [Fact]
    public void DiffLines_EmptyToContent_AllAdded()
    {
        var diff = TextDiff.DiffLines("", "a\nb");
        Assert.All(diff, l => Assert.Equal(DiffLineKind.Added, l.Kind));
        Assert.Equal(2, diff.Count);
    }

    [Fact]
    public void DiffLines_ContentToEmpty_AllRemoved()
    {
        var diff = TextDiff.DiffLines("a\nb", "");
        Assert.All(diff, l => Assert.Equal(DiffLineKind.Removed, l.Kind));
        Assert.Equal(2, diff.Count);
    }

    [Fact]
    public void DiffLines_HandlesCrLfAndNumbersLines()
    {
        var oldText = "aaa\nbbb\r\nccc";
        var newText = "aaa\r\nccc";

        var diff = TextDiff.DiffLines(oldText, newText);

        var removed = diff.Single(l => l.Kind == DiffLineKind.Removed);
        Assert.Equal("bbb", removed.Text);
        Assert.Equal(2, removed.OldNumber);
        Assert.Equal(0, removed.NewNumber);
    }

    [Fact]
    public void ToUnified_MarksLinesWithPlusMinus()
    {
        var diff = TextDiff.DiffLines("a\nb", "a\nc");
        var unified = TextDiff.ToUnified(diff);

        Assert.Contains("+c", unified);
        Assert.Contains("-b", unified);
        Assert.Contains(" a", unified);
    }
}

