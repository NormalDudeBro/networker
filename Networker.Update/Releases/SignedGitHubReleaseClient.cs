using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Networker.Update.Contracts.Releases;
using Networker.Update.Contracts.Versioning;
using Networker.Update.Security;
using NuGet.Versioning;

namespace Networker.Update.Releases;

public sealed class SignedGitHubReleaseClient
{
    private const long MaxMetadataBytes = 2 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly ReleaseFeedVerifier _verifier;

    public SignedGitHubReleaseClient(HttpClient http, ReleaseFeedVerifier verifier)
    {
        _http = http;
        _verifier = verifier;
    }

    public async Task<AvailableRelease?> CheckAsync(string channel, CancellationToken token)
    {
        string endpoint = channel == NetworkerVersionPolicy.StableChannel
            ? NetworkerVersionPolicy.ApiBase + "/releases/latest"
            : NetworkerVersionPolicy.ApiBase + "/releases?per_page=20&page=1";
        byte[] metadata = await GetBytesAsync(new Uri(endpoint), MaxMetadataBytes, token, api: true);
        IReadOnlyList<GitHubRelease> releases = channel == NetworkerVersionPolicy.StableChannel
            ? [JsonSerializer.Deserialize<GitHubRelease>(metadata, JsonOptions()) ?? new()]
            : JsonSerializer.Deserialize<List<GitHubRelease>>(metadata, JsonOptions()) ?? [];

        foreach (GitHubRelease release in releases
            .Select(value => (Release: value, Valid: TryGetVersion(value, channel, out NuGetVersion parsed), Version: parsed))
            .Where(value => value.Valid)
            .OrderByDescending(value => value.Version)
            .Select(value => value.Release))
        {
            NetworkerVersionPolicy.TryParseTag(release.TagName, out NuGetVersion version);
            string releaseChannel = NetworkerVersionPolicy.ChannelFor(version);
            string manifestName = NetworkerVersionPolicy.FeedAssetName(releaseChannel);
            GitHubAsset? manifestAsset = Single(release.Assets, manifestName);
            GitHubAsset? signatureAsset = Single(release.Assets, manifestName + ".sig");
            if (manifestAsset is null || signatureAsset is null) continue;
            byte[] manifestBytes = await GetBytesAsync(ValidateAsset(release.TagName, manifestAsset), MaxMetadataBytes, token);
            byte[] signature = await GetBytesAsync(ValidateAsset(release.TagName, signatureAsset), 64 * 1024, token);
            if (!_verifier.Verify(manifestBytes, signature, out _)) throw new InvalidDataException("Release signature is invalid.");
            ReleaseManifest manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, ManifestOptions())
                ?? throw new InvalidDataException("Release manifest is empty.");
            ValidateManifest(manifest, version, releaseChannel);
            GitHubAsset? package = Single(release.Assets, manifest.FileName);
            if (package is null || package.Size != manifest.Size) throw new InvalidDataException("Release package metadata is inconsistent.");
            return new AvailableRelease(manifest, ValidateAsset(release.TagName, package), release.TagName, release.Name, release.Body, release.PublishedAt);
        }
        return null;
    }

    private static bool TryGetVersion(GitHubRelease release, string channel, out NuGetVersion version)
    {
        version = null!;
        if (release.Draft || !NetworkerVersionPolicy.TryParseTag(release.TagName, out version)) return false;
        bool preview = NetworkerVersionPolicy.IsPreview(version);
        if (release.Prerelease != preview) return false;
        string releaseChannel = NetworkerVersionPolicy.ChannelFor(version);
        return channel == NetworkerVersionPolicy.PreviewChannel
            ? releaseChannel is NetworkerVersionPolicy.StableChannel or NetworkerVersionPolicy.PreviewChannel
            : releaseChannel == NetworkerVersionPolicy.StableChannel;
    }

    public async Task DownloadAsync(AvailableRelease release, string destination, IProgress<int>? progress, CancellationToken token)
    {
        string partial = destination + ".partial";
        try
        {
            using HttpResponseMessage response = await SendAsync(release.PackageUri, token, api: false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Manifest.Size)
                throw new InvalidDataException("Update package size changed.");
            await using Stream input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long written = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer, token);
                if (read == 0) break;
                written += read;
                if (written > release.Manifest.Size) throw new InvalidDataException("Update package exceeded authenticated size.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                progress?.Report((int)(written * 100 / release.Manifest.Size));
            }
            await output.FlushAsync(token);
            string digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (written != release.Manifest.Size || !string.Equals(digest, release.Manifest.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Update package verification failed.");
            File.Move(partial, destination, overwrite: true);
            progress?.Report(100);
        }
        finally { try { File.Delete(partial); } catch { } }
    }

    private static void ValidateManifest(ReleaseManifest manifest, NuGetVersion releaseVersion, string channel)
    {
        if (manifest.Schema != 1 || manifest.PackageId != NetworkerVersionPolicy.PackId
            || manifest.Channel != channel || !NetworkerVersionPolicy.TryParseInformationalVersion(manifest.Version, out var version)
            || version != releaseVersion || manifest.Size <= 0 || manifest.Size > 2L * 1024 * 1024 * 1024
            || manifest.FileName != Path.GetFileName(manifest.FileName) || !manifest.FileName.EndsWith("-win-x64.zip", StringComparison.Ordinal)
            || manifest.Sha256.Length != 64 || manifest.Sha256.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("Release manifest failed policy validation.");
    }

    private async Task<byte[]> GetBytesAsync(Uri uri, long maximum, CancellationToken token, bool api = false)
    {
        using HttpResponseMessage response = await SendAsync(uri, token, api);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maximum) throw new InvalidDataException("Metadata is too large.");
        await using Stream stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, token);
            if (read == 0) break;
            if (output.Length + read > maximum) throw new InvalidDataException("Metadata is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token, bool api)
    {
        Uri current = uri;
        for (int redirect = 0; redirect <= 5; redirect++)
        {
            if (current.Scheme != Uri.UriSchemeHttps || !(api ? current.Host == "api.github.com" : IsDownloadHost(current.Host)))
                throw new InvalidDataException("Untrusted update host.");
            var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("Networker-Launcher/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(api ? "application/vnd.github+json" : "application/octet-stream"));
            HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                Uri? next = response.Headers.Location;
                response.Dispose();
                if (next is null) throw new InvalidDataException("Update redirect has no destination.");
                current = next.IsAbsoluteUri ? next : new Uri(current, next);
                api = false;
                continue;
            }
            return response;
        }
        throw new InvalidDataException("Update exceeded redirect limit.");
    }

    private static bool IsDownloadHost(string host) => host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    private static Uri ValidateAsset(string tag, GitHubAsset asset)
    {
        if (asset.Name != Path.GetFileName(asset.Name) || !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid release asset URL.");
        string prefix = $"/{NetworkerVersionPolicy.RepositoryPath}/releases/download/{tag}/";
        string finalSegment = Uri.UnescapeDataString(uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..]);
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) || finalSegment != asset.Name)
            throw new InvalidDataException("Release asset URL mismatched tag.");
        return uri;
    }

    private static GitHubAsset? Single(List<GitHubAsset>? assets, string name)
    {
        GitHubAsset[] matches = assets?.Where(x => x.Name == name).ToArray() ?? [];
        return matches.Length == 1 ? matches[0] : null;
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static JsonSerializerOptions ManifestOptions() => new() { PropertyNameCaseInsensitive = true };
    private sealed record GitHubRelease
    {
        public string TagName { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string? Body { get; init; }
        public DateTimeOffset PublishedAt { get; init; }
        public bool Draft { get; init; }
        public bool Prerelease { get; init; }
        public List<GitHubAsset>? Assets { get; init; }
    }
    private sealed record GitHubAsset
    {
        public string Name { get; init; } = string.Empty;
        public long Size { get; init; }
        public string BrowserDownloadUrl { get; init; } = string.Empty;
    }
}
