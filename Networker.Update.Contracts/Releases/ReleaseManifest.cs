namespace Networker.Update.Contracts.Releases;

public sealed record ReleaseManifest
{
    public int Schema { get; init; }
    public string PackageId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long Size { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}

public sealed record AvailableRelease(
    ReleaseManifest Manifest,
    Uri PackageUri,
    string TagName,
    string? ReleaseName,
    string? ReleaseNotes,
    DateTimeOffset PublishedAt);
