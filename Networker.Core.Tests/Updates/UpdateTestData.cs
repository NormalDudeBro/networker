using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NuGet.Versioning;
using Networker.Core.Updates;

namespace Networker.Core.Tests.Updates;

/// <summary>
/// Builders for the update test fixtures: releases, assets, checksum sidecars,
/// and MSIX-like ZIP payloads. No network, no certificates.
/// </summary>
public static class UpdateTestData
{
    public static NuGetVersion V(string tag) => NetworkerVersionPolicy.ParseTag(tag);

    public static UpdateRelease Release(string tag, params ReleaseAsset[] assets)
        => new(
            V(tag),
            tag,
            Name: $"Networker {tag}",
            Body: "Release notes for " + tag,
            PublishedAt: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            HtmlUrl: NetworkerVersionPolicy.ReleaseHtmlUrl(V(tag)),
            IsPrerelease: !NetworkerVersionPolicy.IsStable(V(tag)),
            Assets: assets);

    public static ReleaseAsset Asset(string tag, string name, long size = 5_000_000)
        => new(name, size, NetworkerVersionPolicy.AssetDownloadUrl(V(tag), name));

    public static string SidecarLine(string sha256Hex, string fileName)
        => $"{sha256Hex}  {fileName}";

    public static string Sha256Hex(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>
    /// Builds a minimal MSIX-like ZIP whose bytes hash to a stable, computable
    /// value. The manifest can be overridden for identity/XML error cases.
    /// </summary>
    public static byte[] BuildMsix(
        string name = "Networker",
        string publisher = "CN=Kenny",
        string version = "1.0.0.65535",
        string architecture = "x64",
        string? manifestOverride = null,
        bool duplicateManifest = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            string manifest = manifestOverride
                ?? $"<Package><Identity Name=\"{name}\" Publisher=\"{publisher}\" Version=\"{version}\" ProcessorArchitecture=\"{architecture}\"/></Package>";

            WriteEntry(archive, "AppxManifest.xml", manifest);

            if (duplicateManifest)
            {
                WriteEntry(archive, "AppxManifest.xml", manifest);
            }
        }

        return stream.ToArray();
    }

    public static byte[] BuildMsixWithEntry(string entryName, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, entryName, content);
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
