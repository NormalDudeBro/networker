# Networker Updates & Releases

How Networker is distributed, updated, and published. This is the operational guide
for maintainers and the reference for the in-app updater's contract. The authoritative
implementation details live in `Networker.Core/Updates/` and `scripts/`.

## 1. Distribution contract

Networker's installable distribution is a **single, trusted-certificate-signed,
self-contained x64 MSIX**.
There is no portable ZIP, EXE installer, MSIX bundle, x86, or ARM64 artifact, and there
is no custom updater executable. GitHub Releases provide discovery and transport; the
MSIX signing certificate and Windows package identity are the execution trust root.

Every installable release must contain **exactly these four assets** (SemVer
without the leading `v`):

```text
Networker-{semver}-win-x64.msix
Networker-{semver}-win-x64.msix.sha256
Networker-x64.appinstaller
Networker.cer
```

`Networker-x64.appinstaller` is the App Installer manifest for first install and manual
recovery. It deliberately contains **no `UpdateSettings`**: the OS-level App Installer
updater would be a second updater. Networker's in-app scheduler owns all update checks.

`Networker.cer` is the public half of Networker's dedicated self-signed code-signing
identity. Before first installation, an administrator imports it into the local machine's
Trusted People store. The encrypted PFX and its generated password exist only as GitHub
Actions secrets and are never release assets.

The checksum sidecar is a single ASCII line, LF-terminated, no BOM:

```text
{64 lowercase hex characters}  Networker-{semver}-win-x64.msix
```

(two spaces between the hash and the filename; enforced by `UpdateChecksum.cs`).

If trusted signing secrets are unavailable, the tag workflow publishes a clearly marked
source-only GitHub release containing only GitHub's generated source archives. It never
builds or uploads an unsigned package. Source-only releases are not an updater channel:
asset selection ignores them because the required MSIX, checksum, and App Installer assets
are absent.

## 2. Version contract

Tags are strict. Accepted forms:

| Kind | Tag | MSIX version | Example MSIX asset |
|---|---|---|---|
| Stable | `vMAJOR.MINOR.PATCH` | `MAJOR.MINOR.PATCH.65535` | `Networker-1.2.3-win-x64.msix` |
| Preview | `vMAJOR.MINOR.PATCH-preview.N` (`N` in `1..65534`) | `MAJOR.MINOR.PATCH.N` | `Networker-1.2.3-preview.4-win-x64.msix` |

Rejected tags (any of these fails the build before signing):

- no leading `v`, or uppercase `V`
- missing components (`v1`, `v1.2`), or a fourth component (`v1.2.3.4`)
- leading zeros (`v01.2.3`, `v1.02.3`, `v1.2.03`)
- build metadata (`v1.2.3+build`)
- any prerelease label other than `preview.N` (`-alpha.1`, `-preview`, `-preview.1.2`)
- `preview.0` or `preview.65535+`; any numeric component above `65535`

Mapping guarantees (enforced by `NetworkerVersionPolicy` and mirrored by
`Prepare-Release.ps1`, which the workflow cross-checks against the built assembly):

- every preview is older than its final release (`1.2.3.4 < 1.2.3.65535`)
- the next patch is newer than the prior final (`1.2.4.0 > 1.2.3.65535`)

The updater compares normalized semantic versions, never strings, and never offers an
equal or older release.

## 3. Package identity and the publisher freeze

The MSIX package name is immutable:

```text
Name = 12266223-d1a1-43c3-aca2-59c9ae71cd23
ProcessorArchitecture = x64
```

The `Package.appxmanifest` in the repository keeps `Publisher="CN=Kenny"` and
`Version="1.0.0.0"` as **local-development placeholders only**. They are never shipped:
`Prepare-Release.ps1` replaces them in the ephemeral release checkout with the mapped
version and the **subject of the trusted signing certificate**.

The first published release freezes the package identity. Every later release must use
the same package name and the same Publisher subject (see §8 for rotation). The release
workflow enforces this before building by downloading the prior non-draft release's
`.appinstaller` and comparing its `MainPackage` name and publisher.

## 4. GitHub environment and secrets

| Item | Value |
|---|---|
| Repository | `NormalDudeBro/networker` (immutable constants, not user-configurable) |
| Release workflow | `.github/workflows/release.yml`, triggered by `v*` tags only |
| Environment | `production-release` (protected; configure required reviewers/approval) |
| Permissions | `contents: write` (scoped to the job) |
| Runner | `windows-2022` |

Required secrets (repository or environment level):

```text
NETWORKER_SIGNING_CERTIFICATE_BASE64     # base64-encoded code-signing PFX
NETWORKER_SIGNING_CERTIFICATE_PASSWORD   # generated PFX password
NETWORKER_SIGNING_CERTIFICATE_CER_BASE64 # matching public certificate
```

`GITHUB_TOKEN` is automatic with `contents: write` and is used for the release-API
identity check and `gh` publishing.

If the certificate secrets are missing, the workflow publishes a source-only release.
It never creates an unsigned installable package. The PFX is decoded only into
`$RUNNER_TEMP` and is deleted immediately after the signed build. Never commit a PFX;
`*.pfx` is gitignored.

## 5. What the release workflow does

On a `v*` tag push:

1. Checkout the tag (ephemeral copy; the repository tree is never modified).
2. Set up .NET 8; `dotnet restore`; run the full Core test suite.
3. Detect signing secrets. If absent, publish a source-only release and stop the package
   path. If present, materialize the PFX and public certificate into `$RUNNER_TEMP` and
   trust the public certificate on the ephemeral runner.
4. `Prepare-Release.ps1` — validates the strict tag grammar, derives the Publisher
   subject from the PFX, patches `Package.appxmanifest` (Version + Publisher only),
   and emits the exact asset names and mapping.
5. Verify package identity against prior releases:
   - if the tag already has any release (draft or not), fail — releases are immutable;
   - if a prior non-draft release exists, download its `Networker-x64.appinstaller`
     and require identical `MainPackage` name/publisher, else fail.
6. Build the x64 MSIX with MSBuild package signing disabled, sign the completed package
   directly with `signtool /f` using the temporary PFX, then remove the PFX.
7. Cross-check `Prepare-Release.ps1 -SelfTest` against the built `Networker.Core`
   assembly so script and policy cannot drift.
8. Verify the produced package:
   - structural: `AppxSignature.p7x`, `AppxMetadata/CodeIntegrity.cat`,
     `AppxBlockMap.xml` present;
   - packaged-manifest identity: embedded `AppxManifest.xml` must declare exactly the
     prepared name/publisher/`x64`/mapped version;
   - signature trust: `signtool verify /pa /v` must pass after the matching public
     certificate is imported into the runner trust store.
9. Prepare the four assets: copy the MSIX under its exact asset name, write the
   SHA-256 sidecar (byte-exact format from §1), generate `Networker-x64.appinstaller`,
   and include `Networker.cer` for one-time client trust.
10. `gh release create` (draft) → upload all four assets → `gh release edit
    --draft=false --prerelease=<bool>`. Stable releases become latest; previews remain
    prereleases. Any failure before this point leaves nothing public; a failure during
    publishing leaves an invisible draft that never reaches the updater.

Existing tags, releases, and assets are never overwritten.

## 6. Updater behavior (in-app)

- **Startup is non-blocking and offline-safe.** Automatic checks start only after the
  main window is activated, run in the background, and never delay startup, navigation,
  tools, or AI workflows.
- **Channels:** stable by default; previews are opt-in (Settings → APPLICATION UPDATES).
  A channel change is immediately due rather than reusing the other channel's state.
- **Scheduler:** a check runs when the last successful check is at least 24 hours old;
  a six-hour periodic wake lets long-running instances become due. Failed checks back
  off 15 min, 1 hour, 6 hours, then 24 hours; a GitHub rate-limit reset can extend the
  wait. Manual checks bypass time/backoff but still coalesce.
- **Discovery:** only non-draft versioned GitHub Releases are considered, from
  `https://api.github.com/repos/NormalDudeBro/networker`. Source branches, commit
  archives, and Actions artifacts are never update sources. Metadata uses
  `If-None-Match`/ETag with a sanitized cache; `304` is only honored with a valid cache.
- **Download:** checksum first, then the exact `.msix` to
  `TemporaryFolder\NetworkerUpdates\{tag}` with redirect allowlisting (HTTPS only,
  GitHub hosts, max 5 hops), a 1 GiB cap, streaming SHA-256, and atomic finalization.
- **Verification:** sidecar checksum (constant-time compare), then the bounded
  `AppxManifest.xml` inside the package (no extraction) — name, publisher, architecture,
  and mapped version must match.
- **Installation:** Windows App SDK `PackageDeploymentManager` stages the package while
  Networker keeps running. Policy is fixed: `AllowUnsigned=false`,
  `ForceAppShutdown=false`, `ForceTargetAppShutdown=false`,
  `ForceUpdateFromAnyVersion=false`, `RetainFilesOnFailure=false`,
  `DeferRegistrationWhenPackagesAreInUse=true`. The expected digest is passed when
  supported. Only `CompletedSuccess` counts as staged; then the app offers
  `Restart now` / `Later`. Nothing is ever force-closed.
- **States:** `Disabled, Idle, Checking, UpToDate, Available, Downloading, Verifying,
  Installing, RestartRequired, Cancelled, Failed`.

## 7. Failure and recovery behavior

- **Check failures** (network, rate limit, timeout) surface a concise recoverable
  error in Settings, persist the next check time with backoff, and never escape into
  startup or other workflows.
- **Cancelled or partial downloads** are cleaned best-effort from
  `TemporaryFolder\NetworkerUpdates`; invalid/stale files are deleted on the next run.
- **Failed verification or deployment** leaves the currently installed package intact;
  the staged file is discarded. Windows validates the signature and same-publisher rule
  independently of the app.
- **A confirmed staged update** is kept until the next launch confirms the target
  version is installed, then removed.
- **Failed release runs:** a draft release may be left behind if publishing failed
  mid-way. Releases are immutable, so to re-run the same tag, delete the draft first
  (`gh release delete vX.Y.Z --yes`). Or just use a new tag.

## 8. Certificate rotation

The Publisher subject (the PFX's subject DN) is frozen by the first published release
and must never change. Renewal is fine: generate the replacement certificate with the
same subject, upload its PFX/password/public certificate to the secrets, and publish the
new public certificate with the release.
A certificate whose subject differs will fail the release workflow before anything is
published, and Windows would reject a package from a different publisher anyway.

## 9. Paths

| What | Where | Notes |
|---|---|---|
| Settings (channels, times, counters) | `LocalSettings` keys: `AutomaticUpdateChecksEnabled` (default `true`), `IncludePrereleaseUpdates` (default `false`), `LastSuccessfulUpdateCheckUtc`, `LastCheckedUpdateChannel`, `NextAutomaticUpdateCheckUtc`, `UpdateCheckFailureCount` | parsed safely; malformed values fall back to defaults |
| Cache (ETags + last release + dismissed tag) | `LocalFolder\Updates\release-cache.json` | sanitized, atomic; corrupt cache recovers without affecting startup |
| Diagnostics | `LocalFolder\Logs\updates.log` | bounded, rotated; never logs secrets, response bodies, or query-bearing URLs |
| Staging | `TemporaryFolder\NetworkerUpdates\{tag}\` | `.partial` while downloading; `.msix` only after verification |
| User data | `LocalSettings`, `LocalFolder` prompts, `%LOCALAPPDATA%\Networker` vault/templates, configured `NetworkConfigDirectory` | never included in, modified by, or deleted by updates |

## 10. Prerelease testing

- Enable **previews** in Settings → APPLICATION UPDATES.
- Publish `vX.Y.Z-preview.N` through the same release workflow; the tag's `-preview.`
  label marks the release as a prerelease (never `latest`).
- Preview-only users see previews; stable users never see them. Opt-in users get the
  highest semantic version across both channels.
- A preview is always older than its final release (`X.Y.Z.N < X.Y.Z.65535`), so
  upgrading from the last preview to the final works in one step.

## 11. Release validation template

Record the following for each published/verified release (e.g. `docs/updates-validation.md`):

| Field | Value |
|---|---|
| Release tag | `vX.Y.Z` / `vX.Y.Z-preview.N` |
| OS version (tested on) | e.g. `Windows 11 23H2 (build 22631)` |
| Package full name | `Get-AppxPackage | Where Name -eq 12266223-d1a1-43c3-aca2-59c9ae71cd23` → `..._x64__...` |
| Package version | `1.2.3.65535` (preview `1.2.3.4`) |
| Asset digest (SHA-256) | first 64 chars of the sidecar |
| `signtool verify /pa /v` | pass / exit 0 |
| Packaged manifest identity | name / publisher / `x64` / version all match |
| Deployment `ActivityId` | from the in-app update log on the test machine |
| Persistence checks | settings, prompts, vault/templates, custom config dir all survive upgrade |

## 12. End-to-end checklist (first production release)

1. Configure the `production-release` environment and both certificate secrets.
2. Publish `v1.0.0-preview.1`; install via its `.appinstaller`; opt into previews.
3. Create sentinel user data (settings, prompts, vault/templates, custom config dir).
4. Verify offline/blocked startup stays fully usable; only checking shows an error.
5. Publish `v1.0.0-preview.2`; verify discovery, notes, exact x64 asset selection,
   Later/dismiss, cancel/retry with partial cleanup, then a clean staged update.
6. Verify `Restart now` and `Later` both work; user data survives the upgrade.
7. Attempt equal, older, wrong-publisher, unsigned, x86, and invalid-signature
   packages — all must be rejected and the current install left usable.
8. Publish final `v1.0.0`; verify it maps to `1.0.0.65535`, supersedes previews, and
   stable users never see preview releases.
9. Record results in the validation template (§11).
