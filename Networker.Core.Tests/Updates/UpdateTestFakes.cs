using System.Net;
using Networker.Core.Updates;
using NuGet.Versioning;

namespace Networker.Core.Tests.Updates;

/// <summary>
/// In-memory and file-backed fakes for the update contracts, plus an
/// async-capable HTTP handler for streaming/downloader tests.
/// </summary>
public static class UpdateTestFakes
{
    public static InstalledVersion Packaged(string tag, string packageVersion, string publisher = "CN=Kenny")
        => new(
            NetworkerVersionPolicy.ParseTag(tag),
            tag,
            IsPackaged: true,
            PackageName: "Networker",
            PackageFamilyName: "Networker_publisher",
            PackageFullName: "Networker_1.0.0.0_x64__publisher",
            Publisher: publisher,
            PackageVersion: packageVersion,
            Architecture: "x64",
            CanInstallUpdates: true);

    public static InstalledVersion Dev()
        => new(null, "1.0.0-dev", false, null, null, null, null, null, null, false);

    public static InstalledVersion Mismatched(string tag, string packageVersion)
        => new(
            NetworkerVersionPolicy.ParseTag(tag),
            tag,
            IsPackaged: true,
            PackageName: "OtherApp",
            PackageFamilyName: "OtherApp_publisher",
            PackageFullName: "OtherApp_1.0.0.0_x64__publisher",
            Publisher: "CN=Other",
            PackageVersion: packageVersion,
            Architecture: "x64",
            CanInstallUpdates: false);
}

public sealed class FakeReleaseClient : IGitHubReleaseClient
{
    public Queue<ReleaseCheckResult> Results { get; } = new();
    public List<UpdateChannel> CalledChannels { get; } = new();
    public List<string?> CalledEtags { get; } = new();
    public Func<CancellationToken, Task>? Gate { get; set; }
    public Exception? Throw { get; set; }
    public int CallCount => CalledChannels.Count;

    public async Task<ReleaseCheckResult> CheckAsync(UpdateChannel channel, string? etag, CancellationToken cancellationToken)
    {
        CalledChannels.Add(channel);
        CalledEtags.Add(etag);
        if (Throw is not null)
        {
            throw Throw;
        }

        if (Gate is not null)
        {
            await Gate(cancellationToken);
        }

        return Results.Count > 0 ? Results.Dequeue() : new ReleaseCheckResult(null, null, false);
    }
}

public sealed class FakeDownloader : IUpdatePackageDownloader
{
    public Func<SelectedUpdateAssets, string, CancellationToken, Task<DownloadedPackage>>? Handler { get; set; }
    public int CallCount { get; private set; }

    public Task<DownloadedPackage> DownloadAsync(
        UpdateRelease release,
        SelectedUpdateAssets assets,
        string destinationDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        CallCount++;
        if (Handler is not null)
        {
            return Handler(assets, destinationDirectory, cancellationToken);
        }

        string path = Path.Combine(destinationDirectory, assets.MsixAsset.Name);
        return Task.FromResult(new DownloadedPackage(path, new string('a', 64), "placeholder sidecar"));
    }
}

public sealed class FakeVerifier : IUpdatePackageVerifier
{
    public Func<DownloadedPackage, CancellationToken, Task<VerifiedPackage>>? Handler { get; set; }

    public Task<VerifiedPackage> VerifyAsync(
        UpdateRelease release,
        DownloadedPackage downloaded,
        InstalledVersion installed,
        CancellationToken cancellationToken)
    {
        if (Handler is not null)
        {
            return Handler(downloaded, cancellationToken);
        }

        return Task.FromResult(new VerifiedPackage(downloaded.PackagePath, "sha256:" + downloaded.ExpectedSha256Hex));
    }
}

public sealed class FakeInstaller : IUpdateInstaller
{
    public Func<VerifiedPackage, CancellationToken, Task<UpdateInstallResult>>? Handler { get; set; }
    public int CallCount { get; private set; }

    public Task<UpdateInstallResult> InstallAsync(VerifiedPackage package, CancellationToken cancellationToken)
    {
        CallCount++;
        return Handler is not null
            ? Handler(package, cancellationToken)
            : Task.FromResult(new UpdateInstallResult(true, null, null, null));
    }
}

public sealed class MemoryCacheStore : IUpdateCacheStore
{
    public Dictionary<UpdateChannel, string?> Etags { get; } = new();
    public Dictionary<UpdateChannel, UpdateRelease?> Cached { get; } = new();
    public string? DismissedTag { get; set; }

    public string? GetETag(UpdateChannel channel) => Etags.TryGetValue(channel, out var e) ? e : null;
    public void SetETag(UpdateChannel channel, string? etag) => Etags[channel] = etag;
    public UpdateRelease? GetCachedRelease(UpdateChannel channel) => Cached.TryGetValue(channel, out var r) ? r : null;
    public void SetCachedRelease(UpdateChannel channel, UpdateRelease? release) => Cached[channel] = release;
    public string? GetDismissedUpdateTag() => DismissedTag;
    public void SetDismissedUpdateTag(string? tag) => DismissedTag = tag;
}

public sealed class MemoryStorage : IUpdatePackageStorage
{
    public string Root { get; }
    public List<string> Preserved { get; } = new();
    public List<string> Removed { get; } = new();
    public List<string> Cleaned { get; } = new();

    public MemoryStorage(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    public string GetDownloadDirectoryPath(string tag) => Path.Combine(Root, tag);
    public void PreserveStaged(string tag) => Preserved.Add(tag);
    public IReadOnlyList<string> GetStagedTags() => Preserved;
    public void RemoveStaged(string tag) => Removed.Add(tag);
    public void Cleanup(string tag) => Cleaned.Add(tag);
    public void CleanupAll() { }
}

public sealed class TestLog : IUpdateLog
{
    public List<string> Entries { get; } = new();
    public void Info(string message) => Entries.Add("I " + message);
    public void Warn(string message) => Entries.Add("W " + message);
    public void Error(string message, Exception? exception = null) => Entries.Add("E " + message);
    public void Debug(string message) => Entries.Add("D " + message);
}

public sealed class FakeClock : IUpdateClock
{
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

public sealed class TestInstalledVersionProvider : IInstalledVersionProvider
{
    public InstalledVersion Value { get; set; }

    public TestInstalledVersionProvider(InstalledVersion value)
    {
        Value = value;
    }

    public InstalledVersion GetInstalledVersion() => Value;
}

/// <summary>
/// Async-capable handler for streaming, redirect, stall, and cancellation
/// tests. Redirects are returned to the caller (never auto-followed).
/// </summary>
public sealed class AsyncStubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public AsyncStubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _handler(request, cancellationToken);

    public static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler, string baseAddress = "https://api.github.com")
        => new(new AsyncStubHttpMessageHandler(handler))
        {
            BaseAddress = new Uri(baseAddress),
        };

    public static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location);
        return response;
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

    public static HttpResponseMessage Bytes(byte[] content, string url)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };
}

/// <summary>
/// A read-only, non-seekable stream over an in-memory buffer — used to drive
/// the downloader's indeterminate-progress path (StreamContent computes a
/// Content-Length only for seekable streams).
/// </summary>
public sealed class NonSeekableReadStream : Stream
{
    private readonly byte[] _data;
    private int _position;

    public NonSeekableReadStream(byte[] data)
    {
        _data = data;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count)
    {
        int remaining = _data.Length - _position;
        int toCopy = Math.Min(count, remaining);
        Array.Copy(_data, _position, buffer, offset, toCopy);
        _position += toCopy;
        return toCopy;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Stream that delivers one chunk then blocks until cancelled — used to drive
/// the downloader's stall timeout.
/// </summary>
public sealed class SlowStream : Stream
{
    private readonly byte[] _chunk;
    private bool _delivered;

    public SlowStream(byte[] chunk)
    {
        _chunk = chunk;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_delivered)
        {
            _delivered = true;
            _chunk.AsSpan().CopyTo(buffer.Span);
            return _chunk.Length;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
}
