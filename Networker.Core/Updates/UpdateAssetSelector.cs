namespace Networker.Core.Updates;

/// <summary>
/// Validates a release against the frozen asset contract and selects the exact
/// x64 MSIX plus checksum sidecar. Rejects anything outside the contract
/// instead of guessing at the first plausible asset.
/// </summary>
public static class UpdateAssetSelector
{
    /// <summary>Lower bound for a plausible self-contained MSIX (1 MiB).</summary>
    public const long MinPlausibleMsixSize = 1_000_000;

    /// <summary>Upper bound for any downloaded asset (1 GiB).</summary>
    public const long MaxAssetSize = UpdateChecksum.MaxPackageBytes;

    /// <summary>
    /// Selects the exact contract assets for the release, or throws
    /// <see cref="UpdateException"/> describing the contract violation.
    /// </summary>
    public static SelectedUpdateAssets Select(UpdateRelease release)
    {
        if (!IsValidReleaseUrl(release))
        {
            throw new UpdateException("The release URL failed validation.");
        }

        ReleaseAsset? msix = null;
        ReleaseAsset? checksum = null;
        string msixName = NetworkerVersionPolicy.MsixAssetName(release.Version);
        string checksumName = NetworkerVersionPolicy.ChecksumAssetName(release.Version);

        foreach (var asset in release.Assets)
        {
            if (!IsValidAssetUrl(asset.BrowserDownloadUrl, release.TagName))
            {
                throw new UpdateException($"The asset {asset.Name} URL failed validation.");
            }

            if (asset.Name == msixName)
            {
                if (msix is not null)
                {
                    throw new UpdateException("The release contains duplicate MSIX assets.");
                }

                msix = asset;
            }
            else if (asset.Name == checksumName)
            {
                if (checksum is not null)
                {
                    throw new UpdateException("The release contains duplicate checksum assets.");
                }

                checksum = asset;
            }
        }

        if (msix is null)
        {
            throw new UpdateException("The release is missing its x64 MSIX asset.");
        }

        if (checksum is null)
        {
            throw new UpdateException("The release is missing its checksum asset.");
        }

        if (msix.Size <= 0 || msix.Size < MinPlausibleMsixSize || msix.Size > MaxAssetSize)
        {
            throw new UpdateException("The MSIX asset size is implausible.");
        }

        if (checksum.Size <= 0 || checksum.Size > UpdateChecksum.MaxSidecarBytes)
        {
            throw new UpdateException("The checksum asset size is implausible.");
        }

        return new SelectedUpdateAssets(release, msix, checksum);
    }

    /// <summary>The release page URL must be the exact versioned tag URL.</summary>
    public static bool IsValidReleaseUrl(UpdateRelease release)
        => string.Equals(
            release.HtmlUrl,
            NetworkerVersionPolicy.ReleaseHtmlUrl(release.Version),
            StringComparison.Ordinal);

    /// <summary>
    /// Asset URLs must be HTTPS under the immutable release download path for
    /// the exact tag. This blocks source archives and any other path.
    /// </summary>
    public static bool IsValidAssetUrl(string url, string tagName)
        => url.StartsWith(
            NetworkerVersionPolicy.ReleasesDownloadBase + "/" + tagName + "/",
            StringComparison.Ordinal);
}
