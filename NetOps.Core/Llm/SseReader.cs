using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace NetOps.Core.Llm;

public static class SseReader
{
    /// <summary>
    /// Reads Server-Sent Events from a response stream. Each yielded value is the
    /// concatenated payload of one SSE event (all "data:" lines joined with newlines).
    /// Handles the OpenAI-style "[DONE]" sentinel.
    /// </summary>
    public static async IAsyncEnumerable<string> ReadEvents(
        Stream responseStream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(responseStream);
        var buffer = new List<string>();

        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (line.Length == 0)
            {
                if (buffer.Count > 0)
                {
                    yield return string.Join("\n", buffer);
                    buffer.Clear();
                }

                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[5..].TrimStart();
            if (payload == "[DONE]")
            {
                if (buffer.Count > 0)
                {
                    yield return string.Join("\n", buffer);
                    buffer.Clear();
                }

                yield break;
            }

            if (payload.Length > 0)
            {
                buffer.Add(payload);
            }
        }

        if (buffer.Count > 0)
        {
            yield return string.Join("\n", buffer);
        }
    }
}
