using System.Text.Json;

namespace Networker.Core.Updates;

/// <summary>
/// The sanitized per-channel metadata persisted on disk: ETags, the last known
/// release per channel, and the dismissed-tag marker. Contains no secrets.
/// </summary>
public sealed record CacheFileData(
    string? StableEtag,
    string? PreviewEtag,
    CachedReleaseRecord? StableRelease,
    CachedReleaseRecord? PreviewRelease,
    string? DismissedTag)
{
    public static CacheFileData Empty { get; } = new(null, null, null, null, null);
}

/// <summary>
/// Atomic, corruption-tolerant persistence of <see cref="CacheFileData"/>.
/// Reads recover to an empty cache instead of throwing; writes go through a
/// temp file and an atomic rename. Kept in Core so it can be tested with temp
/// paths; the WinUI cache store supplies the application-data path.
/// </summary>
public sealed class UpdateCacheFile
{
    private readonly string _path;

    public UpdateCacheFile(string path)
    {
        _path = path;
    }

    public CacheFileData Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return CacheFileData.Empty;
            }

            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize(json, GitHubReleaseJsonContext.Default.CacheFileData)
                ?? CacheFileData.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return CacheFileData.Empty;
        }
    }

    public void Write(CacheFileData data)
    {
        string? tempPath = null;
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(data, GitHubReleaseJsonContext.Default.CacheFileData);
            tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, overwrite: true);
            tempPath = null;
        }
        finally
        {
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
        }
    }
}
