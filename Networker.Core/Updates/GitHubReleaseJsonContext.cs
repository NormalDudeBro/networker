using System.Collections.Generic;
using System.Text.Json.Serialization;
using NuGet.Versioning;

namespace Networker.Core.Updates;

/// <summary>
/// Source-generated JSON metadata for GitHub release DTOs and the sanitized
/// release cache, so trimmed Release builds never depend on reflection.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubReleaseDto))]
[JsonSerializable(typeof(List<GitHubReleaseDto>))]
[JsonSerializable(typeof(GitHubErrorDto))]
[JsonSerializable(typeof(CachedReleaseRecord))]
[JsonSerializable(typeof(List<CachedAssetRecord>))]
[JsonSerializable(typeof(CacheFileData))]
public sealed partial class GitHubReleaseJsonContext : JsonSerializerContext
{
}

/// <summary>GitHub "release" API object.</summary>
public sealed record GitHubReleaseDto
{
    public string? TagName { get; set; }
    public string? Name { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? HtmlUrl { get; set; }
    public bool Draft { get; set; }
    public bool Prerelease { get; set; }
    public List<GitHubAssetDto>? Assets { get; set; }
}

/// <summary>GitHub "release asset" API object.</summary>
public sealed class GitHubAssetDto
{
    public string? Name { get; set; }
    public long? Size { get; set; }
    public string? BrowserDownloadUrl { get; set; }
}

/// <summary>GitHub error body.</summary>
public sealed class GitHubErrorDto
{
    public string? Message { get; set; }
}

/// <summary>
/// The sanitized subset of a release persisted to the local cache. Contains no
/// credentials or secrets; every URL is already validated before caching.
/// </summary>
public sealed record CachedReleaseRecord(
    string TagName,
    string? Name,
    string? Body,
    DateTimeOffset PublishedAt,
    string HtmlUrl,
    bool Prerelease,
    IReadOnlyList<CachedAssetRecord> Assets)
{
    public UpdateRelease ToUpdateRelease()
    {
        if (!NetworkerVersionPolicy.TryParseTag(TagName, out NuGetVersion version))
        {
            throw new InvalidOperationException($"Cached tag {TagName} failed the version policy.");
        }

        var assets = new List<ReleaseAsset>(Assets.Count);
        foreach (var asset in Assets)
        {
            assets.Add(new ReleaseAsset(asset.Name, asset.Size, asset.BrowserDownloadUrl));
        }

        return new UpdateRelease(
            version,
            TagName,
            Name,
            Body,
            PublishedAt,
            HtmlUrl,
            Prerelease,
            assets);
    }
}

/// <summary>Sanitized cached asset metadata.</summary>
public sealed record CachedAssetRecord(string Name, long Size, string BrowserDownloadUrl);

/// <summary>Conversions between the domain release and the cached record.</summary>
public static class UpdateReleaseCache
{
    public static CachedReleaseRecord FromRelease(UpdateRelease release)
    {
        var assets = new List<CachedAssetRecord>(release.Assets.Count);
        foreach (var asset in release.Assets)
        {
            assets.Add(new CachedAssetRecord(asset.Name, asset.Size, asset.BrowserDownloadUrl));
        }

        return new CachedReleaseRecord(
            release.TagName,
            release.Name,
            release.Body,
            release.PublishedAt,
            release.HtmlUrl,
            release.IsPrerelease,
            assets);
    }
}
