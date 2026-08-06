using Networker.Core.Llm;

namespace Networker.Core.Tests.Llm;

public class CredentialScrubberTests
{
    [Fact]
    public void Scrub_RedactsConfiguredSecrets()
    {
        var result = CredentialScrubber.Scrub(
            "Call the API with xai-abc123def and also xai-abc123def again.",
            new[] { "xai-abc123def", "gemini-secret" });

        Assert.DoesNotContain("xai-abc123def", result);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(result, "\\[REDACTED\\]").Count);
    }

    [Fact]
    public void Scrub_IgnoresShortOrEmptySecrets()
    {
        var result = CredentialScrubber.Scrub("token abc and", new[] { "abc", "", null });
        Assert.Equal("token abc and", result);
    }

    [Fact]
    public void Scrub_NullText_ReturnsNull()
    {
        Assert.Null(CredentialScrubber.Scrub(null!, new[] { "secret" }));
    }
}

