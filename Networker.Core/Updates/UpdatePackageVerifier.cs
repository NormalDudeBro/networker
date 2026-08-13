using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;

namespace Networker.Core.Updates;

/// <summary>
/// Final package verification before Windows deployment: revalidates the
/// checksum sidecar, re-hashes the finalized file with constant-time
/// comparison, and inspects only the bounded root <c>AppxManifest.xml</c>
/// (never extracting or writing anything) for identity, publisher,
/// architecture, and version.
/// </summary>
public sealed class UpdatePackageVerifier : IUpdatePackageVerifier
{
    private const int MaxManifestBytes = 512 * 1024;
    private const string ManifestEntryName = "AppxManifest.xml";

    private readonly IUpdateLog _log;

    public UpdatePackageVerifier(IUpdateLog log)
    {
        _log = log;
    }

    public Task<VerifiedPackage> VerifyAsync(
        UpdateRelease release,
        DownloadedPackage downloaded,
        InstalledVersion installed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The sidecar must still parse and name the exact downloaded file.
        if (!UpdateChecksum.TryParseSidecar(downloaded.SidecarContent, Path.GetFileName(downloaded.PackagePath), out string sidecarDigest))
        {
            throw new UpdateException("The checksum file is invalid.");
        }

        if (!string.Equals(sidecarDigest, downloaded.ExpectedSha256Hex, StringComparison.Ordinal))
        {
            throw new UpdateException("The checksum file is inconsistent.");
        }

        // Re-hash the finalized file and compare constant-time (guards against
        // any swap between download and deployment).
        using (var file = new FileStream(downloaded.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[81920];
            while (true)
            {
                int read = file.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
            }

            if (!UpdateChecksum.DigestMatches(downloaded.ExpectedSha256Hex, hash.GetHashAndReset()))
            {
                _log.Warn("Package failed final checksum verification.");
                throw new UpdateException("The package checksum verification failed.");
            }
        }

        ManifestIdentity manifest = ReadBoundedManifest(downloaded.PackagePath, cancellationToken);
        ValidateManifest(manifest, release, installed);

        _log.Info($"Verified package {Path.GetFileName(downloaded.PackagePath)} for {release.TagName}.");
        return Task.FromResult(new VerifiedPackage(
            downloaded.PackagePath,
            UpdateChecksum.FormatExpectedDigest(downloaded.ExpectedSha256Hex)));
    }

    private sealed record ManifestIdentity(
        string Name,
        string Publisher,
        string Version,
        string ProcessorArchitecture);

    /// <summary>
    /// Reads exactly one root <c>AppxManifest.xml</c> entry with a bounded,
    /// DTD-disabled XML reader. No entry is extracted or written to disk.
    /// </summary>
    private static ManifestIdentity ReadBoundedManifest(string packagePath, CancellationToken cancellationToken)
    {
        using var archiveStream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        ZipArchiveEntry? manifest = null;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(entry.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase))
            {
                if (manifest is not null)
                {
                    throw new UpdateException("The package contains more than one root manifest.");
                }

                manifest = entry;
            }
        }

        if (manifest is null)
        {
            throw new UpdateException("The package has no root manifest.");
        }

        if (manifest.Length > MaxManifestBytes)
        {
            throw new UpdateException("The package manifest is too large.");
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxManifestBytes,
        };

        using var entryStream = manifest.Open();
        using var reader = XmlReader.Create(entryStream, settings);

        string? name = null;
        string? publisher = null;
        string? version = null;
        string? processorArchitecture = null;
        bool sawIdentity = false;

        try
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Identity")
                {
                    if (sawIdentity)
                    {
                        throw new UpdateException("The package manifest has multiple identity elements.");
                    }

                    sawIdentity = true;
                    name = reader.GetAttribute("Name");
                    publisher = reader.GetAttribute("Publisher");
                    version = reader.GetAttribute("Version");
                    processorArchitecture = reader.GetAttribute("ProcessorArchitecture");
                }
            }
        }
        catch (XmlException ex)
        {
            throw new UpdateException("The package manifest is malformed.", innerException: ex);
        }

        if (!sawIdentity || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(publisher)
            || string.IsNullOrEmpty(version) || string.IsNullOrEmpty(processorArchitecture))
        {
            throw new UpdateException("The package manifest is missing identity information.");
        }

        return new ManifestIdentity(name, publisher, version, processorArchitecture);
    }

    private static void ValidateManifest(ManifestIdentity manifest, UpdateRelease release, InstalledVersion installed)
    {
        if (!string.Equals(manifest.ProcessorArchitecture, "x64", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateException("The package is not built for x64.");
        }

        if (!string.IsNullOrEmpty(installed.PackageName)
            && !string.Equals(manifest.Name, installed.PackageName, StringComparison.Ordinal))
        {
            throw new UpdateException("The package identity does not match the installed application.");
        }

        if (!string.IsNullOrEmpty(installed.Publisher)
            && !string.Equals(manifest.Publisher, installed.Publisher, StringComparison.Ordinal))
        {
            throw new UpdateException("The package publisher does not match the installed application.");
        }

        string mappedTarget = NetworkerVersionPolicy.ToMsixVersion(release.Version).ToString(4);
        if (!string.Equals(manifest.Version, mappedTarget, StringComparison.Ordinal))
        {
            throw new UpdateException("The package version does not match the release.");
        }

        if (installed.SemanticVersion is not null && release.Version <= installed.SemanticVersion)
        {
            throw new UpdateException("The update is not newer than the installed version.");
        }

        if (!string.IsNullOrEmpty(installed.PackageVersion)
            && Version.TryParse(installed.PackageVersion, out var installedFourPart)
            && Version.TryParse(manifest.Version, out var targetFourPart)
            && targetFourPart <= installedFourPart)
        {
            throw new UpdateException("The package is not newer than the installed package.");
        }
    }
}
