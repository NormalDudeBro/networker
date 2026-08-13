using System;
using System.IO;
using Networker.Core.Updates;

namespace networker.Services.Updates
{
    /// <summary>
    /// <see cref="IUpdateCacheStore"/> backed by an atomically persisted,
    /// sanitized JSON file under <c>LocalFolder\Updates\release-cache.json</c>.
    /// Missing or corrupt cache files recover to an empty cache.
    /// </summary>
    public sealed class UpdateCacheStore : IUpdateCacheStore
    {
        private const string FileName = "release-cache.json";
        private readonly UpdateCacheFile _cacheFile;
        private readonly object _gate = new();

        public UpdateCacheStore()
        {
            string directory = Path.Combine(AppSettings.GetLocalDataDirectory(), "Updates");
            _cacheFile = new UpdateCacheFile(Path.Combine(directory, FileName));
        }

        public string? GetETag(UpdateChannel channel)
        {
            CacheFileData data = Read();
            return channel == UpdateChannel.Preview ? data.PreviewEtag : data.StableEtag;
        }

        public void SetETag(UpdateChannel channel, string? etag)
        {
            CacheFileData data = Read();
            Write(channel == UpdateChannel.Preview
                ? data with { PreviewEtag = etag }
                : data with { StableEtag = etag });
        }

        public UpdateRelease? GetCachedRelease(UpdateChannel channel)
        {
            CacheFileData data = Read();
            CachedReleaseRecord? record = channel == UpdateChannel.Preview ? data.PreviewRelease : data.StableRelease;
            return record is null ? null : record.ToUpdateRelease();
        }

        public void SetCachedRelease(UpdateChannel channel, UpdateRelease? release)
        {
            CacheFileData data = Read();
            CachedReleaseRecord? record = release is null ? null : UpdateReleaseCache.FromRelease(release);
            Write(channel == UpdateChannel.Preview
                ? data with { PreviewRelease = record }
                : data with { StableRelease = record });
        }

        public string? GetDismissedUpdateTag() => Read().DismissedTag;

        public void SetDismissedUpdateTag(string? tag)
            => Write(Read() with { DismissedTag = tag });

        private CacheFileData Read()
        {
            lock (_gate)
            {
                return _cacheFile.Read();
            }
        }

        private void Write(CacheFileData data)
        {
            lock (_gate)
            {
                _cacheFile.Write(data);
            }
        }
    }
}
