using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Networker.Core.Updates;

/// <summary>
/// Downloads the checksum sidecar first, then the MSIX with incremental SHA-256
/// verification, size/stall/overall limits, manual HTTPS redirect validation,
/// and atomic finalization. All writes stay in the provided destination
/// directory; partial files are removed on failure.
/// </summary>
public sealed class UpdatePackageDownloader : IUpdatePackageDownloader
{
    private static readonly HashSet<string> AllowedRedirectHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "api.github.com",
        "release-assets.githubusercontent.com",
        "objects.githubusercontent.com",
    };

    private const int MaxRedirects = 5;
    private const int BufferSize = 81920;

    private readonly HttpClient _http;
    private readonly IUpdateLog _log;
    private readonly TimeSpan _stallTimeout;
    private readonly TimeSpan _overallTimeout;

    public UpdatePackageDownloader(
        HttpClient http,
        IUpdateLog log,
        TimeSpan? stallTimeout = null,
        TimeSpan? overallTimeout = null)
    {
        _http = http;
        _log = log;
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);
        _overallTimeout = overallTimeout ?? TimeSpan.FromMinutes(30);
    }

    public async Task<DownloadedPackage> DownloadAsync(
        UpdateRelease release,
        SelectedUpdateAssets assets,
        string destinationDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);
        string msixName = assets.MsixAsset.Name;
        string finalPath = Path.Combine(destinationDirectory, msixName);
        string partialPath = finalPath + ".partial";

        // Checksum first, bounded to the sidecar size limit.
        string sidecar;
        using (var sidecarCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            sidecarCts.CancelAfter(TimeSpan.FromSeconds(30));
            sidecar = await DownloadBoundedAsync(assets.ChecksumAsset.BrowserDownloadUrl, UpdateChecksum.MaxSidecarBytes, sidecarCts.Token)
                .ConfigureAwait(false);
        }

        if (!UpdateChecksum.TryParseSidecar(sidecar, msixName, out string expectedSha256))
        {
            throw new UpdateException("The checksum file is invalid.");
        }

        try
        {
            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overallCts.CancelAfter(_overallTimeout);

            using var response = await GetDownloadResponseAsync(assets.MsixAsset.BrowserDownloadUrl, overallCts.Token)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new UpdateException($"The download returned HTTP {(int)response.StatusCode}.");
            }

            using var stream = await response.Content.ReadAsStreamAsync(overallCts.Token).ConfigureAwait(false);
            using var file = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);

            long? contentLength = assets.MsixAsset.Size > 0
                ? assets.MsixAsset.Size
                : (long?)response.Content.Headers.ContentLength;
            if (contentLength is { } len && (len <= 0 || len > UpdateChecksum.MaxPackageBytes))
            {
                throw new UpdateException("The package size is implausible.");
            }

            var buffer = new byte[BufferSize];
            long totalRead = 0;
            stallCts.CancelAfter(_stallTimeout);

            while (true)
            {
                int read = await stream.ReadAsync(buffer, stallCts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (totalRead > UpdateChecksum.MaxPackageBytes)
                {
                    throw new UpdateException("The package exceeds the 1 GiB limit.");
                }

                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), stallCts.Token).ConfigureAwait(false);
                stallCts.CancelAfter(_stallTimeout);
                if (contentLength is { } cl && cl > 0)
                {
                    progress?.Report((double)totalRead / cl);
                }
                else
                {
                    progress?.Report(-1);
                }
            }

            if (contentLength is { } expected && totalRead != expected)
            {
                throw new UpdateException("The download size does not match the release metadata.");
            }

            byte[] computed = hash.GetHashAndReset();
            if (!UpdateChecksum.DigestMatches(expectedSha256, computed))
            {
                _log.Warn($"Package {msixName} failed checksum verification.");
                throw new UpdateException("The package checksum verification failed.");
            }

            file.Flush(flushToDisk: true);
            file.Dispose();
            File.Move(partialPath, finalPath);
            _log.Info($"Downloaded {msixName}: {totalRead} bytes, checksum verified.");
            return new DownloadedPackage(finalPath, expectedSha256, sidecar);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryDelete(partialPath);
            _log.Warn($"Download of {msixName} timed out or stalled.");
            throw new UpdateException("The download timed out.");
        }
        catch (HttpRequestException ex)
        {
            TryDelete(partialPath);
            _log.Warn($"Download of {msixName} failed ({ex.GetType().Name}).");
            throw new UpdateException("Network failure during the download.", innerException: ex);
        }
        catch (UpdateException)
        {
            TryDelete(partialPath);
            throw;
        }
        catch (IOException ex)
        {
            TryDelete(partialPath);
            throw new UpdateException("The download could not be written to disk.", innerException: ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDelete(partialPath);
            throw new UpdateException("The download could not be written to disk.", innerException: ex);
        }
    }

    /// <summary>
    /// Fetches a small payload (the checksum sidecar) with a hard byte cap.
    /// </summary>
    private async Task<string> DownloadBoundedAsync(string url, int maxBytes, CancellationToken cancellationToken)
    {
        using var response = await GetDownloadResponseAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new UpdateException($"The checksum download returned HTTP {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes)
        {
            throw new UpdateException("The checksum file is too large.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                throw new UpdateException("The checksum file is too large.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Follows up to five redirects manually, requiring HTTPS and an allowlisted
    /// host on every hop and rejecting HTTPS-to-HTTP downgrades. Returns the
    /// final response; the caller disposes it.
    /// </summary>
    private async Task<HttpResponseMessage> GetDownloadResponseAsync(string url, CancellationToken cancellationToken)
    {
        string current = url;
        HttpResponseMessage? response = null;
        try
        {
            for (int hop = 0; ; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!IsRedirect(response.StatusCode))
                {
                    return response;
                }

                string? next = null;
                if (response.Headers.Location is { } location)
                {
                    next = location.IsAbsoluteUri
                        ? location.AbsoluteUri
                        : new Uri(new Uri(current), location).AbsoluteUri;
                }

                response.Dispose();
                response = null;

                if (hop >= MaxRedirects || next is null)
                {
                    throw new UpdateException("The download redirect chain is invalid or too long.");
                }

                if (!IsAllowedDownloadUrl(next))
                {
                    throw new UpdateException("The download redirect target is not allowed.");
                }

                current = next;
            }
        }
        catch
        {
            response?.Dispose();
            throw;
        }
    }

    private static bool IsRedirect(HttpStatusCode status)
        => status == HttpStatusCode.Moved
           || status == HttpStatusCode.Found
           || status == HttpStatusCode.SeeOther
           || status == HttpStatusCode.TemporaryRedirect
           || status == HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// HTTPS only, host in the GitHub allowlist (a plain scheme check rejects
    /// any HTTPS-to-HTTP downgrade).
    /// </summary>
    private static bool IsAllowedDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AllowedRedirectHosts.Contains(uri.Host);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup only.
        }
    }
}
