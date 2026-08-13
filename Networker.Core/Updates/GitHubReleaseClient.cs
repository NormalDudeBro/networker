using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Networker.Core.Updates;

/// <summary>
/// HTTP client for the GitHub Releases API of the fixed production repository.
/// Produces validated domain releases; never touches application data.
/// </summary>
public sealed class GitHubReleaseClient : IGitHubReleaseClient
{
    private const string StablePath = "/repos/" + NetworkerVersionPolicy.RepositoryPath + "/releases/latest";
    private const string PreviewPath = "/repos/" + NetworkerVersionPolicy.RepositoryPath + "/releases?per_page=100";
    private const string GitHubApiVersion = "2022-11-28";

    private readonly HttpClient _http;
    private readonly IInstalledVersionProvider _installedVersion;
    private readonly IUpdateLog _log;

    public GitHubReleaseClient(HttpClient http, IInstalledVersionProvider installedVersion, IUpdateLog log)
    {
        _http = http;
        _installedVersion = installedVersion;
        _log = log;
    }

    public async Task<ReleaseCheckResult> CheckAsync(UpdateChannel channel, string? etag, CancellationToken cancellationToken)
    {
        string path = channel == UpdateChannel.Stable ? StablePath : PreviewPath;
        if (_http.BaseAddress is null)
        {
            throw new UpdateException("Update client has no base address.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_http.BaseAddress, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubApiVersion);
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            $"Networker/{_installedVersion.GetInstalledVersion().DisplayVersion} (+https://github.com/{NetworkerVersionPolicy.RepositoryPath})");
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            string? nextEtag = response.Headers.ETag?.Tag;

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _log.Info($"Release check {channel}: 304 Not Modified.");
                return new ReleaseCheckResult(null, nextEtag, true);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _log.Info($"Release check {channel}: no releases published (404).");
                return new ReleaseCheckResult(null, nextEtag, false);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden
                || response.StatusCode == HttpStatusCode.Unauthorized
                || (int)response.StatusCode == 429)
            {
                DateTimeOffset? retryAfter = ParseRateLimit(response);
                _log.Warn($"Release check {channel}: rate limited (HTTP {(int)response.StatusCode}).");
                throw new UpdateException(
                    "The update service is rate limited.",
                    retryAfterUtc: retryAfter);
            }

            if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
            {
                _log.Warn($"Release check {channel}: unexpected HTTP {(int)response.StatusCode}.");
                throw new UpdateException($"The update service returned HTTP {(int)response.StatusCode}.");
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            UpdateRelease? release;
            try
            {
                release = channel == UpdateChannel.Stable
                    ? MapStable(json)
                    : MapPreview(json);
            }
            catch (JsonException ex)
            {
                _log.Warn($"Release check {channel}: invalid JSON response.");
                throw new UpdateException("The update service returned an invalid response.", innerException: ex);
            }

            _log.Info($"Release check {channel}: {(release is null ? "no eligible release" : "release " + release.TagName)}.");
            return new ReleaseCheckResult(release, nextEtag, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _log.Warn($"Release check {channel}: request timed out.");
            throw new UpdateException("The update check timed out.");
        }
        catch (HttpRequestException ex)
        {
            _log.Warn($"Release check {channel}: network failure ({ex.GetType().Name}).");
            throw new UpdateException("Network failure during the update check.", innerException: ex);
        }
    }

    private UpdateRelease? MapStable(string json)
    {
        var dto = JsonSerializer.Deserialize(json, GitHubReleaseJsonContext.Default.GitHubReleaseDto);
        return dto is null ? null : Map(dto, UpdateChannel.Stable);
    }

    private UpdateRelease? MapPreview(string json)
    {
        var list = JsonSerializer.Deserialize(json, GitHubReleaseJsonContext.Default.ListGitHubReleaseDto);
        if (list is null || list.Count == 0)
        {
            return null;
        }

        UpdateRelease? best = null;
        foreach (var dto in list)
        {
            if (dto is null)
            {
                continue;
            }

            UpdateRelease? mapped = Map(dto, UpdateChannel.Preview);
            if (mapped is null)
            {
                continue;
            }

            if (best is null || mapped.Version > best.Version)
            {
                best = mapped;
            }
        }

        return best;
    }

    /// <summary>
    /// Maps a DTO to the domain, skipping drafts, invalid tags, prerelease/tag
    /// mismatches, channel-ineligible releases, and invalid URLs. A skipped
    /// release is logged and never surfaces to the user.
    /// </summary>
    private UpdateRelease? Map(GitHubReleaseDto dto, UpdateChannel channel)
    {
        if (dto.Draft)
        {
            _log.Info($"Skipped draft release {dto.TagName ?? "(unknown)"}.");
            return null;
        }

        if (!NetworkerVersionPolicy.TryParseTag(dto.TagName, out NuGet.Versioning.NuGetVersion version))
        {
            _log.Info($"Skipped release with invalid tag {dto.TagName ?? "(unknown)"}.");
            return null;
        }

        string tagName = dto.TagName!;

        bool isPreview = NetworkerVersionPolicy.IsPreview(version);
        if (dto.Prerelease != isPreview)
        {
            _log.Info($"Skipped release {dto.TagName}: GitHub prerelease flag disagrees with the tag.");
            return null;
        }

        if (channel == UpdateChannel.Stable && !NetworkerVersionPolicy.IsStable(version))
        {
            _log.Info($"Skipped prerelease {dto.TagName} on the stable channel.");
            return null;
        }

        string expectedHtmlUrl = NetworkerVersionPolicy.ReleaseHtmlUrl(version);
        if (!string.Equals(dto.HtmlUrl, expectedHtmlUrl, StringComparison.Ordinal))
        {
            _log.Warn($"Skipped release {dto.TagName}: html_url failed validation.");
            return null;
        }

        if (dto.PublishedAt is null || dto.PublishedAt.Value == default)
        {
            _log.Info($"Skipped release {dto.TagName}: missing publication date.");
            return null;
        }

        var assets = new List<ReleaseAsset>();
        if (dto.Assets is not null)
        {
            foreach (var asset in dto.Assets)
            {
                if (asset is null || string.IsNullOrEmpty(asset.Name))
                {
                    continue;
                }

                long size = asset.Size ?? 0;
                assets.Add(new ReleaseAsset(asset.Name, size, asset.BrowserDownloadUrl ?? string.Empty));
            }
        }

        return new UpdateRelease(
            version,
            tagName,
            dto.Name,
            dto.Body,
            dto.PublishedAt.Value,
            dto.HtmlUrl!,
            isPreview,
            assets);
    }

    private DateTimeOffset? ParseRateLimit(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return DateTimeOffset.UtcNow + delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            return date;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values))
        {
            foreach (string value in values)
            {
                if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long unix)
                    && unix > 0)
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unix);
                }
            }
        }

        return null;
    }
}
