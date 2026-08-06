using System.Text;

namespace Networker.Core.NetTools.Config;

/// <summary>
/// Small helpers for emitting the newline-terminated lines that the
/// reference templates produce (the reference environment is configured with
/// <c>trim_blocks=True, lstrip_blocks=True, keep_trailing_newline=False</c>).
/// </summary>
internal static class ConfigWriter
{
    public static void W(StringBuilder sb, string text) => sb.Append(text).Append('\n');
}
