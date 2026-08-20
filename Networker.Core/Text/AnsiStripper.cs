using System.Text;

namespace Networker.Core.Text;

/// <summary>
/// Removes ANSI/VT escape sequences from terminal text while preserving all
/// printable characters. Covers CSI sequences (<c>ESC [ … final</c>), OSC
/// sequences (<c>ESC ] … BEL|ST</c>), and the common single/intermediate
/// character escapes used by shells and build tools.
/// </summary>
public static class AnsiStripper
{
    /// <summary>Returns <paramref name="value"/> with every escape sequence removed.</summary>
    public static string Strip(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        int index = 0;
        int length = value.Length;
        while (index < length)
        {
            char current = value[index];
            if (current == '\x1b')
            {
                index = SkipEscape(value, index + 1);
                continue;
            }
            // Single-byte CSI / OSC introducers (rare in modern output).
            if (current == '\x9b')
            {
                index = SkipUntil(value, index + 1, IsCsiFinal);
                continue;
            }
            builder.Append(current);
            index++;
        }
        return builder.ToString();
    }

    /// <summary>
    /// Returns the index just past the escape sequence that begins at
    /// <paramref name="start"/> (which points at the byte following ESC).
    /// </summary>
    private static int SkipEscape(string value, int start)
    {
        if (start >= value.Length) return start;
        char introducer = value[start];
        switch (introducer)
        {
            case '[': // CSI: ESC [ params* intermediates? final (0x40..0x7E)
                return SkipUntil(value, start + 1, IsCsiFinal);
            case ']': // OSC: ESC ] … BEL (0x07) or ST (ESC \)
                for (int i = start + 1; i < value.Length; i++)
                {
                    if (value[i] == '\x07') return i + 1;
                    if (value[i] == '\x1b' && i + 1 < value.Length && value[i + 1] == '\\') return i + 2;
                }
                return value.Length;
            case '(':
            case ')':
            case '#':
            case '*':
            case '+':
            case '-':
            case '.':
            case '/':
            case '%': // ESC X Y — one intermediate plus one final byte
                return Math.Min(start + 2, value.Length);
            default: // Single-character escape (ESC 7, ESC 8, ESC D, ESC M, ESC c …)
                return start + 1;
        }
    }

    private static int SkipUntil(string value, int from, Func<char, bool> predicate)
    {
        for (int i = from; i < value.Length; i++)
        {
            if (predicate(value[i])) return i + 1;
        }
        return value.Length;
    }

    private static bool IsCsiFinal(char value) => value is >= '\x40' and <= '\x7e';
}
