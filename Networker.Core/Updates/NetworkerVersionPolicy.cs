using System.Globalization;
using NuGet.Versioning;

namespace Networker.Core.Updates;

/// <summary>
/// The frozen update contract: strict release-tag grammar, SemVer ordering,
/// SemVer-to-MSIX version mapping, and exact asset names. All release/asset
/// decisions in the updater and the release pipeline must flow through here.
/// </summary>
public static class NetworkerVersionPolicy
{
    /// <summary>The GitHub owner; never user-configurable in production.</summary>
    public const string Owner = "NormalDudeBro";

    /// <summary>The GitHub repository; never user-configurable in production.</summary>
    public const string Repository = "networker";

    /// <summary>"owner/repository" path used for GitHub API and release URLs.</summary>
    public const string RepositoryPath = Owner + "/" + Repository;

    /// <summary>Base of the GitHub REST API for this repository.</summary>
    public const string ApiBase = "https://api.github.com/repos/" + RepositoryPath;

    /// <summary>Base of immutable release download URLs.</summary>
    public const string ReleasesDownloadBase = "https://github.com/" + RepositoryPath + "/releases/download";

    /// <summary>Release URL prefix for a release tag.</summary>
    public const string ReleasesTagBase = "https://github.com/" + RepositoryPath + "/releases/tag";

    /// <summary>The only architecture the updater acquires.</summary>
    public const string ArchitectureToken = "win-x64";

    public const string MsixExtension = ".msix";
    public const string ChecksumExtension = ".sha256";

    /// <summary>The MSIX revision reserved for stable releases.</summary>
    public const int StableRevision = 65535;

    /// <summary>The upper bound for the numeric preview label (65534 keeps the MSIX revision within a uint16).</summary>
    public const int MaxPreviewNumber = 65534;

    /// <summary>The largest value any MSIX version component may take.</summary>
    public const int MaxVersionComponent = 65535;

    /// <summary>
    /// Parses and strictly validates a release tag. Accepts only
    /// <c>vMAJOR.MINOR.PATCH</c> (stable) and <c>vMAJOR.MINOR.PATCH-preview.N</c>
    /// with <c>N</c> in 1..65534. Rejects missing <c>v</c>, build metadata,
    /// leading-zero numeric identifiers, other prerelease labels, and components
    /// above 65535.
    /// </summary>
    public static bool TryParseTag(string? tag, out NuGetVersion version)
    {
        version = null!;
        if (string.IsNullOrEmpty(tag) || tag.Length < 2 || tag[0] != 'v')
        {
            return false;
        }

        string body = tag[1..];
        if (!IsStrictRawBody(body))
        {
            return false;
        }

        if (!NuGetVersion.TryParse(body, out var parsed) || !IsAcceptable(parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    /// <summary>
    /// Validates the raw tag body (without the leading <c>v</c>) against the
    /// frozen grammar before handing it to NuGet, because NuGetVersion
    /// normalizes silently: it fills in missing components, accepts four-part
    /// versions, and ignores build metadata.
    /// </summary>
    private static bool IsStrictRawBody(string body)
    {
        if (body.Contains('+', StringComparison.Ordinal))
        {
            return false; // no build metadata
        }

        string core = body;
        string? release = null;
        int dash = body.IndexOf('-');
        if (dash >= 0)
        {
            core = body[..dash];
            release = body[(dash + 1)..];
        }

        return IsExactCore(core) && (release is null || IsPreviewLabel(release));
    }

    /// <summary>Exactly three dot-separated decimal segments in the uint16 range, without leading zeros.</summary>
    private static bool IsExactCore(string core)
    {
        string[] segments = core.Split('.');
        if (segments.Length != 3)
        {
            return false;
        }

        foreach (string segment in segments)
        {
            if (!IsUnsignedDecimal(segment, min: 0, max: MaxVersionComponent))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Exactly "preview.N" with N in 1..MaxPreviewNumber.</summary>
    private static bool IsPreviewLabel(string release)
    {
        const string prefix = "preview.";
        if (!release.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return IsUnsignedDecimal(release[prefix.Length..], min: 1, max: MaxPreviewNumber);
    }

    private static bool IsUnsignedDecimal(string text, int min, int max)
    {
        if (text.Length == 0 || text.Length > 5)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        // Leading zeros are rejected outright, so "0" is the only zero form.
        if (text.Length > 1 && text[0] == '0')
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value >= min && value <= max;
    }

    /// <summary>
    /// Parses a tag or throws <see cref="ArgumentException"/> when it violates
    /// the strict release grammar.
    /// </summary>
    public static NuGetVersion ParseTag(string tag)
    {
        if (!TryParseTag(tag, out NuGetVersion version))
        {
            throw new ArgumentException($"Invalid release tag: {tag}.", nameof(tag));
        }

        return version;
    }

    /// <summary>
    /// Parses an assembly informational version (no leading <c>v</c>, as emitted
    /// by the release pipeline) against the same strict grammar. Rejects
    /// developer labels such as <c>1.0.0-dev</c> so dev builds never register as
    /// a release version.
    /// </summary>
    public static bool TryParseInformationalVersion(string? informational, out NuGetVersion version)
    {
        version = null!;
        if (string.IsNullOrEmpty(informational) || !IsStrictRawBody(informational))
        {
            return false;
        }

        if (!NuGetVersion.TryParse(informational, out var parsed) || !IsAcceptable(parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    /// <summary>Whether the release labels are exactly the allowed <c>preview.N</c> form.</summary>
    public static bool IsPreview(NuGetVersion version)
    {
        var labels = version.ReleaseLabels.ToList();
        if (labels.Count != 2 || labels[0] != "preview")
        {
            return false;
        }

        string number = labels[1];
        if (number.Length == 0 || (number.Length > 1 && number[0] == '0'))
        {
            return false;
        }

        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            && n >= 1 && n <= MaxPreviewNumber;
    }

    /// <summary>Whether the version has no prerelease labels.</summary>
    public static bool IsStable(NuGetVersion version) => !version.ReleaseLabels.Any();

    /// <summary>Whether the version is a stable or an acceptable preview.</summary>
    public static bool IsAcceptable(NuGetVersion version) => IsStable(version) || IsPreview(version);

    /// <summary>
    /// Maps a validated release version to the four-part MSIX version. Stable
    /// <c>1.2.3</c> becomes <c>1.2.3.65535</c>; <c>1.2.3-preview.4</c> becomes
    /// <c>1.2.3.4</c>, guaranteeing every preview sorts below its final release
    /// and that release order is monotonically upgradable.
    /// </summary>
    public static Version ToMsixVersion(NuGetVersion version)
    {
        if (version.Major < 0 || version.Major > MaxVersionComponent
            || version.Minor < 0 || version.Minor > MaxVersionComponent
            || version.Patch < 0 || version.Patch > MaxVersionComponent)
        {
            throw new ArgumentException("Version component out of the MSIX uint16 range.", nameof(version));
        }

        int revision = IsStable(version)
            ? StableRevision
            : int.Parse(version.ReleaseLabels.ElementAt(1), CultureInfo.InvariantCulture);
        return new Version(version.Major, version.Minor, version.Patch, revision);
    }

    /// <summary>The exact MSIX asset name for a release, without the leading <c>v</c>.</summary>
    public static string MsixAssetName(NuGetVersion version)
        => $"Networker-{version.ToNormalizedString()}-{ArchitectureToken}{MsixExtension}";

    /// <summary>The exact checksum sidecar name for a release.</summary>
    public static string ChecksumAssetName(NuGetVersion version)
        => $"{MsixAssetName(version)}{ChecksumExtension}";

    /// <summary>The HTTP download URL for a release asset.</summary>
    public static string AssetDownloadUrl(NuGetVersion version, string assetName)
        => $"{ReleasesDownloadBase}/v{version.ToNormalizedString()}/{assetName}";

    /// <summary>The release page URL for a tag.</summary>
    public static string ReleaseHtmlUrl(NuGetVersion version)
        => $"{ReleasesTagBase}/v{version.ToNormalizedString()}";

    /// <summary>
    /// True when the tag refers to a release asset of the exact asset contract.
    /// </summary>
    public static bool IsContractAssetName(string assetName, NuGetVersion version)
        => assetName == MsixAssetName(version) || assetName == ChecksumAssetName(version);
}
