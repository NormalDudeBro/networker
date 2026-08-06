using System;
using System.Collections.Generic;
using System.Linq;

namespace NetOps.Core.Llm;

public static class CredentialScrubber
{
    /// <summary>
    /// Redacts configured secrets from text before it is sent to any provider,
    /// so keys never leak into prompts, logs, or remote sidecars.
    /// </summary>
    public static string Scrub(string text, IEnumerable<string?> secrets)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var secret in secrets.Where(s => !string.IsNullOrWhiteSpace(s) && s!.Length >= 4))
        {
            text = text.Replace(secret!, "[REDACTED]", StringComparison.Ordinal);
        }

        return text;
    }
}
