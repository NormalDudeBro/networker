# Networker Updates and Releases

## Distribution Contract

Networker ships for x64 Windows as one per-user installer:

```text
Networker-Setup.exe
```

Normal users download and run that file only. Setup installs under
`%LOCALAPPDATA%\Networker.Desktop`, creates a Start Menu shortcut, and offers an unchecked
desktop shortcut. Uninstall removes application slots but deliberately leaves
`%LOCALAPPDATA%\Networker` and configured workspace data intact.

The installer is built with Inno Setup. Velopack was prototyped first, as planned, but its
Windows apply path could delete the renamed prior version before a failed second rename was
recovered. That did not meet Networker's automatic-recovery gate, so production uses the
documented Inno plus A/B fallback.

Each GitHub Release contains exactly four updater assets:

```text
Networker-Setup.exe
Networker-{semver}-win-x64.zip
releases.{channel}.json
releases.{channel}.json.sig
```

`channel` is `win-x64` for stable releases and `preview-win-x64` for preview releases.
Production automation fails rather than publishing if Authenticode or feed-signing material
is missing.

## Version Contract

Accepted tags are `vMAJOR.MINOR.PATCH` and `vMAJOR.MINOR.PATCH-preview.N`, with no leading
zeros or build metadata. Tag-derived SemVer is used by all assemblies, `version.txt`, the ZIP
name, release manifest, and GitHub Release. Stable is newer than previews with the same core
version. Equal and older releases are never installed. Preview opt-in considers preview builds
and newer stable finals; switching away from preview never downgrades automatically.

## Runtime Architecture

- Root `Networker.exe` is a small NativeAOT bootstrap and permanent shortcut target.
- `active-slot.txt` contains exactly `app-a` or `app-b`.
- Each slot is a complete version containing `Networker.Launcher.exe`, `networker.exe`, and
  `Networker.UpdateHost.exe`.
- The launcher checks state before starting the WinUI process. Cached launches do no network
  I/O; due checks have a two-second metadata deadline.
- The signed ZIP is downloaded with bounded size, streamed SHA-256 verification, HTTPS host
  allowlisting, and a maximum of five validated redirects.
- A temporary copy of UpdateHost waits for launcher/app exit, verifies the ZIP again, safely
  extracts into an inactive staging directory, verifies required files/version, renames the
  inactive slot, and writes `active-slot.txt` last.
- The new app must write a random-token health marker after its WinUI root loads. Two failed
  health attempts restore the previous slot pointer and relaunch it.

A crash before the pointer write leaves the old slot selected. A crash immediately after the
pointer write is reconstructed from the recovery journal on the next launch. User files are
never stored in either application slot.

## Trust Model

The installer, embedded uninstaller, bootstrap, launcher, app, UpdateHost, and Networker-owned assemblies are
signed with a publicly trusted Authenticode code-signing certificate and RFC 3161 timestamp.
This certificate is distinct from the update-feed key.

The release manifest uses ECDSA P-256/SHA-256 over its exact UTF-8 bytes. The launcher pins
the SubjectPublicKeyInfo and key ID through assembly metadata at production build time. An
empty/invalid pin fails closed: Networker starts the current application without checking or
installing an update. Package SHA-256 and size are authenticated fields in that manifest.
The launcher also persists the highest authenticated version per channel to reject signed
feed replay below a previously observed release.

## State and Data

| Purpose | Path |
|---|---|
| Install root | `%LOCALAPPDATA%\Networker.Desktop` |
| Active slot pointer | `%LOCALAPPDATA%\Networker.Desktop\active-slot.txt` |
| Launcher state | `%LOCALAPPDATA%\Networker\Updates\launcher-state.json` |
| Recovery journal | `%LOCALAPPDATA%\Networker\Updates\recovery.json` |
| Downloads | `%LOCALAPPDATA%\Networker\Updates\Downloads` |
| Health markers | `%LOCALAPPDATA%\Networker\Updates\health` |
| Launcher log | `%LOCALAPPDATA%\Networker\Logs\launcher.log` |
| App settings/data | `%LOCALAPPDATA%\Networker` and configured workspace paths |

Launcher state is schema-versioned, interprocess-locked, and atomically replaced. Logging is
bounded and strips query data. Update archive extraction rejects rooted/traversal paths and
limits entry count and uncompressed size.

## Legacy MSIX Migration

First-run launcher UI detects only the frozen package identity
`12266223-d1a1-43c3-aca2-59c9ae71cd23`, publisher `CN=Kenny`, x64. With consent it exports an
allowlisted settings/file payload through `ApplicationDataManager`, protects the metadata
with current-user DPAPI in a user-only ACL directory, verifies file hashes, and imports only
values missing from unpackaged settings. The MSIX remains installed until the unpackaged app
reports healthy; only then is removal attempted with application data preserved. Failed or
cancelled migration leaves the old installation available and the encrypted export intact.

## Release Operations

The protected `production-release` GitHub environment requires:

```text
NETWORKER_SIGNING_CERTIFICATE_BASE64
NETWORKER_SIGNING_CERTIFICATE_PASSWORD
NETWORKER_UPDATE_FEED_PRIVATE_KEY_PEM_BASE64
NETWORKER_UPDATE_FEED_PUBLIC_KEY_SPKI_BASE64
NETWORKER_UPDATE_FEED_KEY_ID
```

Configure or rotate them with:

```powershell
# Generate and upload a new ECDSA feed key; preserve existing Authenticode secrets.
./scripts/Set-GitHubReleaseSecrets.ps1

# Also replace Authenticode secrets with a publicly trusted code-signing PFX.
./scripts/Set-GitHubReleaseSecrets.ps1 -ReplaceAuthenticodeSecrets `
  -SigningCertificatePath C:\secure\networker-code-signing.pfx `
  -SigningCertificatePassword (Read-Host 'PFX password')
```

The script sends values directly to `gh secret set`, never writes private key material to the
repository, and validates the PFX private key and code-signing EKU. Configure required reviewers
and restrict deployment branches/tags in the `production-release` environment settings before
the first public release.

The tag workflow validates the tag, restores and runs both test projects, installs pinned
Inno Setup, materializes keys only in runner temp, embeds the feed public key, signs binaries,
builds ZIP/manifest/Setup, signs exact manifest bytes, verifies everything, creates a draft,
uploads all four assets, re-downloads and re-verifies them, then publishes. Any missing secret
or verification failure leaves no public installable release.

Local unsigned prototype packaging is allowed only for engineering validation:

```powershell
./scripts/New-NetworkerPackage.ps1 -Version 1.2.3 -Channel win-x64
./scripts/Test-ReleaseArtifacts.ps1 -Directory artifacts/release -Version 1.2.3
```

Unsigned output must never be uploaded as a production release.

## Validation Matrix

Before the first production cutover, validate on clean Windows 10 22H2 and Windows 11 VMs:

- fresh install, Start Menu launch, optional desktop shortcut, standard-user operation;
- offline cached launch and due-check timeout;
- stable and preview updates, cancel/retry, large/slow download;
- process kill/power loss before extraction, during extraction, before pointer write, and
  immediately after pointer write;
- disk full, antivirus lock, corrupt ZIP, traversal archive, wrong hash/signature/key/tag;
- two failed health launches restore the prior slot;
- MSIX migration success, cancel, corrupt source, removal failure, and retry;
- settings, prompts, vault, templates, custom workspace, and uninstall preservation;
- Authenticode verification for Setup and installed Networker-owned binaries.
