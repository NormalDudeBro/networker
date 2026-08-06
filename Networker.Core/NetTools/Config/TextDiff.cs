namespace Networker.Core.NetTools.Config;

public enum DiffLineKind
{
    Equal,
    Added,
    Removed,
}

public sealed class DiffLine
{
    public required DiffLineKind Kind { get; init; }
    public required string Text { get; init; }
    public int OldNumber { get; init; }
    public int NewNumber { get; init; }

    public string Marker => Kind switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "-",
        _ => " ",
    };
}

public static class TextDiff
{
    /// <summary>
    /// Computes a line-level diff between two texts using prefix/suffix trimming
    /// plus a Myers shortest-edit-script on the differing middle section.
    /// </summary>
    public static IReadOnlyList<DiffLine> DiffLines(string oldText, string newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        var (head, tailOld, tailNew) = TrimCommon(oldLines, newLines);

        var middle = DiffCore(oldLines[head..tailOld], newLines[head..tailNew], head);
        var result = new List<DiffLine>(oldLines.Length + newLines.Length);

        for (var i = 0; i < head; i++)
        {
            result.Add(new DiffLine
            {
                Kind = DiffLineKind.Equal,
                Text = oldLines[i],
                OldNumber = i + 1,
                NewNumber = i + 1,
            });
        }

        result.AddRange(middle);

        var oldOffset = tailOld;
        var newOffset = tailNew;
        var remaining = oldLines.Length - tailOld;
        for (var i = 0; i < remaining; i++)
        {
            result.Add(new DiffLine
            {
                Kind = DiffLineKind.Equal,
                Text = oldLines[oldOffset + i],
                OldNumber = oldOffset + i + 1,
                NewNumber = newOffset + i + 1,
            });
        }

        return result;
    }

    /// <summary>
    /// Renders a unified-style diff. Lines are prefixed with their marker
    /// (+ / - / space); no hunk headers are emitted.
    /// </summary>
    public static string ToUnified(IReadOnlyList<DiffLine> lines)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            sb.Append(line.Marker).AppendLine(line.Text);
        }

        return sb.ToString();
    }

    private static (int Head, int TailOld, int TailNew) TrimCommon(string[] oldLines, string[] newLines)
    {
        var head = 0;
        while (head < oldLines.Length && head < newLines.Length &&
               oldLines[head] == newLines[head])
        {
            head++;
        }

        var tailOld = oldLines.Length;
        var tailNew = newLines.Length;
        while (tailOld > head && tailNew > head && oldLines[tailOld - 1] == newLines[tailNew - 1])
        {
            tailOld--;
            tailNew--;
        }

        return (head, tailOld, tailNew);
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        return text.Replace("\r\n", "\n").Split('\n');
    }

    private static List<DiffLine> DiffCore(string[] a, string[] b, int lineOffset)
    {
        var n = a.Length;
        var m = b.Length;

        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = a[i] == b[j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var result = new List<DiffLine>(n + m);
        var oldLine = lineOffset + 1;
        var newLine = lineOffset + 1;
        var i2 = 0;
        var j2 = 0;
        while (i2 < n && j2 < m)
        {
            if (a[i2] == b[j2])
            {
                result.Add(new DiffLine
                {
                    Kind = DiffLineKind.Equal,
                    Text = a[i2],
                    OldNumber = oldLine,
                    NewNumber = newLine,
                });
                i2++;
                j2++;
                oldLine++;
                newLine++;
            }
            else if (dp[i2 + 1, j2] >= dp[i2, j2 + 1])
            {
                result.Add(new DiffLine
                {
                    Kind = DiffLineKind.Removed,
                    Text = a[i2],
                    OldNumber = oldLine,
                });
                i2++;
                oldLine++;
            }
            else
            {
                result.Add(new DiffLine
                {
                    Kind = DiffLineKind.Added,
                    Text = b[j2],
                    NewNumber = newLine,
                });
                j2++;
                newLine++;
            }
        }

        while (i2 < n)
        {
            result.Add(new DiffLine
            {
                Kind = DiffLineKind.Removed,
                Text = a[i2],
                OldNumber = oldLine,
            });
            i2++;
            oldLine++;
        }

        while (j2 < m)
        {
            result.Add(new DiffLine
            {
                Kind = DiffLineKind.Added,
                Text = b[j2],
                NewNumber = newLine,
            });
            j2++;
            newLine++;
        }

        return result;
    }
}

