using System.Globalization;
using NuGet.Versioning;

namespace Networker.Update.Contracts.Versioning;

public static class NetworkerVersionPolicy
{
    public const string Owner = "NormalDudeBro";
    public const string Repository = "networker";
    public const string RepositoryPath = Owner + "/" + Repository;
    public const string RepositoryUrl = "https://github.com/" + RepositoryPath;
    public const string ApiBase = "https://api.github.com/repos/" + RepositoryPath;
    public const string PackId = "Networker.Desktop";
    public const string StableChannel = "win-x64";
    public const string PreviewChannel = "preview-win-x64";

    public static bool TryParseTag(string? tag, out NuGetVersion version)
    {
        version = null!;
        if (string.IsNullOrEmpty(tag) || tag.Length < 2 || tag[0] != 'v') return false;
        return TryParseBody(tag[1..], out version);
    }

    public static NuGetVersion ParseTag(string tag) => TryParseTag(tag, out var version)
        ? version
        : throw new ArgumentException($"Invalid release tag: {tag}.", nameof(tag));

    public static bool TryParseInformationalVersion(string? value, out NuGetVersion version)
        => TryParseBody(value, out version);

    public static bool IsStable(NuGetVersion version) => !version.IsPrerelease;

    public static bool IsPreview(NuGetVersion version)
    {
        var labels = version.ReleaseLabels.ToArray();
        return labels.Length == 2
            && labels[0] == "preview"
            && IsDecimal(labels[1], 1, int.MaxValue);
    }

    public static string ChannelFor(NuGetVersion version) => IsPreview(version) ? PreviewChannel : StableChannel;

    public static string FeedAssetName(string channel) => $"releases.{channel}.json";

    public static string FeedSignatureAssetName(string channel) => FeedAssetName(channel) + ".sig";

    private static bool TryParseBody(string? body, out NuGetVersion version)
    {
        version = null!;
        if (string.IsNullOrEmpty(body) || body.Contains('+', StringComparison.Ordinal)) return false;

        string core = body;
        string? release = null;
        int dash = body.IndexOf('-');
        if (dash >= 0)
        {
            core = body[..dash];
            release = body[(dash + 1)..];
        }

        string[] segments = core.Split('.');
        if (segments.Length != 3 || segments.Any(x => !IsDecimal(x, 0, int.MaxValue))) return false;
        if (release is not null)
        {
            const string prefix = "preview.";
            if (!release.StartsWith(prefix, StringComparison.Ordinal)
                || !IsDecimal(release[prefix.Length..], 1, int.MaxValue)) return false;
        }

        if (!NuGetVersion.TryParse(body, out var parsed)
            || (!IsStable(parsed) && !IsPreview(parsed))) return false;

        version = parsed;
        return true;
    }

    private static bool IsDecimal(string value, int minimum, int maximum)
    {
        if (value.Length == 0 || value.Length > 10 || (value.Length > 1 && value[0] == '0')) return false;
        foreach (char c in value) if (!char.IsAsciiDigit(c)) return false;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            && parsed >= minimum && parsed <= maximum;
    }
}
