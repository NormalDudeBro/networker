# Networker Installer and Updater Architecture Plan

## Implementation Outcome

Phase 2 rejected Velopack 1.2.0 after source/fault-path review confirmed its Windows apply cleanup could remove the renamed prior directory before a failed second rename was automatically repaired. The implementation therefore uses the plan's mandatory fallback: Inno Setup for the per-user one-file installer, a stable NativeAOT root bootstrap, complete `app-a`/`app-b` slots, an out-of-process UpdateHost, and an atomic `active-slot.txt` commit. The signed-feed, launcher isolation, version, state, health rollback, release workflow, and guided MSIX migration requirements remain as specified below; Velopack-specific project/file descriptions are retained as the historical decision record rather than the final implementation.

## Context

Networker currently ships as a self-contained x64 MSIX from `NormalDudeBro/networker`. Fresh installation requires users to download multiple release assets, manually trust a self-signed certificate, and understand App Installer/MSIX concepts. Updates are coordinated inside the running WinUI application after its main window is activated.

The target experience is one `Networker-Setup.exe` for installation, normal Start Menu launch through a small independent pre-launch updater, stable GitHub Release discovery, safe verified updates, and automatic launch of the current application. Offline or update failures must never prevent Networker from launching.

This document is an architecture and phased implementation plan. Large implementation changes must not begin until the audit, technology comparison, migration decision, and signing decision are complete.

## Codebase Analysis

### Confirmed solution and runtime

- `C:\Users\Kenny\source\repos\networker\networker.sln` currently contains exactly three projects: WinUI app `networker.csproj`, UI-independent `Networker.Core\Networker.Core.csproj`, and xUnit `Networker.Core.Tests\Networker.Core.Tests.csproj`.
- `networker.csproj:3-14` is a `WinExe` targeting `net8.0-windows10.0.19041.0`, minimum Windows `10.0.17763.0`, with WinUI enabled.
- Current app dependencies are `Microsoft.Extensions.DependencyInjection`/`Http` 8.0.1, Windows SDK Build Tools `10.0.28000.2526`, Windows App SDK `2.3.1`, and `Networker.Core`; there is no existing third-party installer/update framework to preserve.
- Release is currently x64, self-contained, and Windows App SDK self-contained (`networker.csproj:64-68`). Trimming and ReadyToRun are disabled because WinUI/WinRT activation and reproducible restore/publish are sensitive to them.
- Although the solution still declares x86/ARM64 configurations, production publishing is explicitly x64.

Current release properties:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
  <AppxPackageSigningEnabled>false</AppxPackageSigningEnabled>
</PropertyGroup>
```

### Current version source

- `Directory.Build.props:10-15` supplies local version `1.0.0-dev` to all projects and fixed assembly/file versions.
- The tag workflow overrides version metadata from strict tags. Stable `vMAJOR.MINOR.PATCH` and preview `vMAJOR.MINOR.PATCH-preview.N` are mapped by `scripts\Prepare-Release.ps1` and `Networker.Core\Updates\NetworkerVersionPolicy.cs`.
- Current MSIX stable mapping uses revision `65535` (for example semantic `1.0.0` becomes MSIX `1.0.0.65535`). This mapping is MSIX-specific and should not leak into the replacement installer/update protocol.

### Current MSIX packaging and friction

- `networker.csproj:11-12,41-48` enables single-project MSIX tooling.
- `Package.appxmanifest:10-13` fixes package name `12266223-d1a1-43c3-aca2-59c9ae71cd23`, placeholder publisher `CN=Kenny`, and placeholder version `1.0.0.0`.
- The manifest requires `runFullTrust` and defines Windows tile/splash assets.
- `.github\workflows\release.yml` currently produces four explicit assets: MSIX, checksum, App Installer manifest, and `Networker.cer`.
- The current certificate is project-generated/self-signed. Release notes instruct users to run elevated PowerShell to import `Networker.cer` into `LocalMachine\TrustedPeople`, then open the App Installer file. This certificate bootstrap is the primary fresh-install friction and also creates SmartScreen/reputation limitations.
- The release workflow is sophisticated but MSIX-specific: it patches manifest identity, freezes package publisher, signs an MSIX, inspects `AppxSignature.p7x`, creates an App Installer document, and verifies package-specific identity rules.

### Current update startup behavior

- `App.xaml.cs:65-111` registers the entire update stack inside the WinUI app's DI container.
- `App.xaml.cs:118-135` creates and activates `MainWindow` first, then starts update cleanup/scheduling without awaiting it:

```csharp
m_window = new MainWindow();
m_window.Activate();

try
{
    UpdateCoordinator coordinator = Services.GetRequiredService<UpdateCoordinator>();
    coordinator.CleanupConfirmedStaged();
    Services.GetRequiredService<UpdateScheduler>().Start();
}
catch (Exception ex)
{
    Services.GetRequiredService<IUpdateLog>().Error("Update startup failed.", ex);
}
```

- `MainWindow.xaml.cs:31,51-57,296-300,330-337` subscribes to in-process update state, shows availability UI, and stops the scheduler on window close.
- Therefore the current updater is neither independent nor pre-launch. It cannot replace the currently running app files and delegates installation to Windows MSIX deployment.

### Current CI/CD

- `.github\workflows\ci.yml` restores the solution, runs Core tests, builds Debug x64, and publishes Release x64 on `windows-2022`.
- `.github\workflows\release.yml` triggers on `v*`, validates the tag, runs tests, builds/signs/verifies, creates a draft, uploads assets, then publishes it.
- Existing tag parsing, immutable-release checks, GitHub environment secrets, draft-before-publish discipline, artifact checksums, and release-source constants are valuable patterns to retain even if the package format changes.

### Audit result

- Reuse strict SemVer/tag parsing and ordering from `Networker.Core\Updates\NetworkerVersionPolicy.cs`, release filtering/ETag/rate-limit patterns from `GitHubReleaseClient.cs`, cadence/backoff from `UpdateSchedulerPolicy.cs`, and bounded diagnostic conventions from `UpdateLogFile.cs`.
- Replace rather than adapt `UpdatePackageDownloader.cs`, `UpdatePackageVerifier.cs`, `UpdateAssetSelector.cs`, `UpdateCoordinator.cs`, and their package records/contracts. They are deliberately MSIX/checksum/AppxManifest-shaped; retaining them would create a second installer beside Velopack.
- Remove the seven `Services\Updates\` adapters only after the new channel is proven. They are package-context/MSIX/application-host implementations, not a reusable out-of-process updater.
- Unpackaged WinUI is already supported by `Properties\PublishProfiles\win-x64.pubxml` and `app.manifest`; no audited application feature has a hard MSIX activation dependency outside persistence/update adapters.
- Framework research used maintained project source and release metadata. Velopack is MIT and active; WiX, Inno, and NSIS remain viable installer-only alternatives; Squirrel's release/maintenance posture is weaker for a new system.
- Critical caveat: Velopack 1.2.0's Windows apply implementation stages the new tree and renames old `current` aside, but its own second-rename failure text says manual repair may be necessary. Production approval is conditional on fault-injection recovery tests in Phase 2.

### Persistence and packaged/unpackaged compatibility

- `C:\Users\Kenny\source\repos\networker\Properties\launchSettings.json` already supports both `MsixPackage` and unpackaged `Project` launches, and `C:\Users\Kenny\source\repos\networker\app.manifest` explicitly declares Windows 10 compatibility for unpackaged Windows App SDK features. The application is not fundamentally dependent on package activation.
- `C:\Users\Kenny\source\repos\networker\AppSettings.cs` deliberately abstracts packaged and unpackaged settings:
  - Packaged mode uses `ApplicationData.Current.LocalSettings` and `ApplicationData.Current.LocalFolder`.
  - Unpackaged fallback uses files beneath `%LOCALAPPDATA%\Networker`.
  - `GetLocalDataDirectory()` and temporary-directory helpers catch package-context failures and return unpackaged paths.
- This abstraction is important but creates a migration boundary: existing MSIX LocalSettings/LocalFolder data lives in the package container, while the new unpackaged installer will read `%LOCALAPPDATA%\Networker`. The migration tool must export/copy package-container values and files before uninstalling MSIX.
- User-owned data already outside the package container is naturally preserved:
  - `AppSettings.NetworkConfigDirectory` defaults to `%LOCALAPPDATA%\Networker`.
  - Vault: `vault.dat` under the configured network config directory (`App.xaml.cs:72-75`).
  - Custom templates: `custom_templates.json` in the same directory.
- Troubleshooting workspace, prompt files, update cache, and update logs use `AppSettings.GetLocalDataDirectory()`, so packaged users may currently have those in the MSIX LocalFolder and need one-time migration.
- No installer or updater may delete `%LOCALAPPDATA%\Networker` or a user-selected `NetworkConfigDirectory` during upgrade/uninstall.

### Current update components and UI surface

- Core update logic is split across 13 files in `C:\Users\Kenny\source\repos\networker\Networker.Core\Updates\` with matching tests in `Networker.Core.Tests\Updates\`. It includes semantic tag policy, GitHub metadata/ETag caching, asset selection, streaming download, checksum parsing, package verification, scheduler policy, and coordination.
- Windows/app adapters are seven files in `C:\Users\Kenny\source\repos\networker\Services\Updates\`, including `MsixUpdateInstaller.cs`, `InstalledVersionProvider.cs`, `UpdateScheduler.cs`, restart, storage, cache, and logging.
- `C:\Users\Kenny\source\repos\networker\Views\SettingsPage.xaml:223-284` exposes a substantial in-app MSIX update UI: stable/preview toggles, manual check, release notes, download/install, cancel, Later, and Restart.
- `SettingsPage.xaml.cs:266-483` directly orchestrates `UpdateCoordinator`, MSIX installation, and restart state. Under the target architecture, Settings should become a read-only/status-and-preferences surface; pre-launch update application belongs to the independent launcher/updater.
- Preserve useful language and policies (stable default, advanced preview opt-in, concise failures, last-check diagnostics), but do not keep two competing update engines.

### User decisions

- Default installation scope: **per user, no administrator requirement**. Install beneath `%LOCALAPPDATA%`, write current-user Start Menu/uninstall registrations, and update without UAC. Do not offer per-machine scope initially because it would require privilege escalation during automatic updates and double the test matrix.
- Production signing: **publicly trusted Authenticode code-signing certificate**. Sign the setup executable, updater/launcher, main executable, and update packages. The current self-signed MSIX certificate is transitional and must not define the new UX.
- Existing-user transition: **one guided migration**. New Setup detects an installed MSIX, preserves/migrates data, closes the old app, removes the old package, installs the new channel, and avoids duplicate shortcuts. Do not allow long-term side-by-side MSIX and unpackaged installations.
- Desktop shortcut: **optional and unchecked**. Velopack shortcut locations are package-time metadata, so package Start Menu only. A signed launcher first-run screen offers the unchecked desktop option.
- Channels: **stable by default; preview retained as an advanced explicit opt-in**. Stable users never consume prereleases.
- Due-check latency: **hard two-second metadata ceiling**. Cached launches perform no network work; due failures immediately open the current app.

## Approach

### Technology comparison

| Technology | One-file installer | Auto-update | Independent pre-launch updater | GitHub Releases | Signing | Complexity | Recommendation |
|---|---|---|---|---|---|---|---|
| **Velopack** | Yes, generated Setup EXE | Built in; full and delta packages | Yes. A tiny Networker launcher can call the managed API, while Velopack's separate native `Update.exe` waits for exit and swaps versions | Built-in GitHub source and custom `IUpdateSource` extension point | Authenticode hooks plus Networker feed signature adapter | Medium | **Recommended for a gated prototype**. Best fit for per-user/no-UAC setup, deltas, locking, shortcuts, uninstall, and GitHub automation without owning a custom replacement engine. MIT, active stable release `1.2.0` (2026-06-03). Its apply interruption caveat must pass Phase 2 before production approval. |
| **WiX 7 Burn/MSI** | Yes, bootstrapper EXE | No consumer auto-updater; must add one | Only by designing/maintaining a separate launcher/service | Custom | Excellent MSI/Burn signing support | High | Strong enterprise installer, poor fit for invisible per-user updates. MSI repair/upgrade semantics and UAC-oriented machine installs solve the wrong default problem. Could package a custom updater but adds no value over Velopack. |
| **Inno Setup 7** | Yes | No built-in update engine | Custom launcher/updater required | Custom | Supports signed setup/uninstaller | Medium-high | Excellent simple installer and mature OSS project, but Networker would still own download, locking, transactional replacement, rollback, and deltas. Use only if Velopack prototype fails WinUI deployment. |
| **NSIS** | Yes | No built-in reliable update engine | Custom/plugin-based | Custom | Supported through plugins/commands | High | Flexible and small, but script/plugin maintenance and a custom updater create more security/recovery burden than Inno or Velopack. |
| **MSIX + App Installer** | App Installer/MSIX, not the desired conventional one-file EXE without a bootstrapper | OS-supported | OS owns updates, not a Networker pre-launch updater | Yes | Strong when signed by public certificate | Medium | A public certificate would eliminate manual trust, but package identity, App Installer infrastructure, migration, and behavior remain MSIX-specific. Does not match the required independent Networker pre-launch flow and still has installation UX constraints. |
| **Squirrel.Windows** | Yes | Built in | Separate `Update.exe` | Yes | Supported | Medium | Historical predecessor, but its latest stable GitHub release is `2.0.1` from 2020 and maintenance/activity is materially weaker. Velopack is the modern maintained successor with better performance/deltas. Do not start new infrastructure on Squirrel. |
| **Custom bootstrapper/updater** | Yes, if built | Entirely custom | Yes | Entirely custom | Entirely custom | Very high | Rejected. Networker would own process coordination, atomic swaps, rollback, repair, shortcuts, uninstall, package extraction, and years of edge cases. Existing Core code is good policy code but not a transactional installer engine. |

Additional comparison details:

- **Install scope/admin:** Velopack, Inno, and NSIS can install per-user without admin. WiX/MSI is strongest for per-machine/admin. MSIX is per-user but trust/bootstrap friction remains outside the Store. Networker chooses per-user only for the first replacement channel.
- **Silent install:** all shortlisted native installers support silent modes; exact Velopack Setup flags must be pinned and smoke-tested against the selected stable version before production.
- **Upgrade/recovery:** Velopack owns an update lock, `.partial` downloads, package verification, wait-for-exit application, and repair-install rollback. Its normal apply does not prove automatic restoration for every second-rename/power-loss failure; that exact gap is a production gate. WiX/MSI has transactional install but no lightweight consumer update discovery. Inno/NSIS require custom recovery orchestration.
- **Rollback:** Velopack can restart the old locator when apply fails before replacement and Networker can retain a prior full package. It does not provide documented post-update health rollback. Do not claim that capability until the prototype implements and tests a recovery journal. MSI rollback is transaction-scoped, not a complete GitHub update system.
- **Delta updates:** Velopack generates/applies deltas and falls back to full packages. Squirrel also supports deltas. The other candidates need external/custom differential packaging.
- **WinUI 3 compatibility:** Networker already publishes a self-contained **unpackaged** app (`Properties\PublishProfiles\win-x64.pubxml` sets `WindowsPackageType=None`), so the installer only needs to deploy a normal directory. Velopack is UI-framework agnostic and packages compiler output. A prototype must still verify XAML/WinRT activation on clean Windows 10/11 VMs.
- **Size:** the app is already self-contained (~large relative to the updater); Velopack's native updater/metadata overhead is small, and deltas reduce recurring transfer. Installer choice will not eliminate the self-contained Windows App SDK payload.
- **SmartScreen:** installer technology does not create reputation. A publicly trusted OV/EV Authenticode certificate, timestamped signatures, consistent publisher identity, and accumulated reputation are required. EV can improve initial reputation handling but is not a guarantee against warnings.
- **Long-term burden:** Velopack is MIT, active, framework/language agnostic, and isolates Networker from replacement mechanics. WiX, Inno, and NSIS are mature but require a second updater design. Squirrel is not preferred for a new system.
- **Licensing:** Velopack and Squirrel are MIT; NSIS uses a permissive zlib/libpng-style license; WiX uses its own reciprocal open-source terms; Inno Setup's source/distribution terms permit broad use but must be reviewed for the pinned release. Preserve the selected tool's license/third-party notices and repeat review on upgrades.

### Recommended architecture

Use **Velopack for installer/package/update mechanics**, with a small Networker-owned launcher and signed metadata adapter for policy/security.

#### Component 1: `Networker-Setup.exe`

- Generated by pinned stable `vpk` from the self-contained unpackaged x64 publish output.
- Freeze Velopack package ID as **`Networker.Desktop`**, yielding the default per-user install root `%LOCALAPPDATA%\Networker.Desktop`. Never use package ID `Networker`: `%LOCALAPPDATA%\Networker` already contains user data, while Velopack uninstall owns and deletes its package root.
- Main packaged/shortcut executable is `Networker.Launcher.exe`, not `networker.exe`. This guarantees all normal Start Menu/desktop launches pass through the independent updater.
- Package only a Start Menu shortcut. On first launch, a small signed screen offers `Create a desktop shortcut`, unchecked by default; create a current-user `.lnk` to the stable root launcher/stub only with consent, and do not recreate a user-deleted shortcut during updates.
- Registers current-user Apps & Features uninstall. Supports unattended install/uninstall using the selected Velopack version's documented flags.
- Includes self-contained .NET 8 and Windows App SDK runtime payload from current publish settings, so no separate framework installer or admin action is normally required.
- On existing MSIX detection, runs the guided migration transaction described below before removing the old package.

#### Component 2: `Networker.Launcher.exe`

- New small `net8.0-windows` x64 `WinExe` project without WinUI, Windows App SDK, or application dependencies. It references `Networker.Update`/minimal Core policy and Velopack only.
- Calls `VelopackApp.Build().Run()` at the earliest possible entry point to process install/update/uninstall/restart hooks.
- Uses a named single-instance mutex for launch/update coordination. Concurrent invocation forwards or waits briefly, then activates/starts the existing main instance rather than running two update checks.
- Reads installed version from Velopack locator/package metadata, not `networker.exe` while replacing files. Cross-checks it against launcher `AssemblyInformationalVersion` for diagnostics.
- Reads update preferences/cache from `%LOCALAPPDATA%\Networker\Updates\launcher-state.json`, atomically written with schema version, stable channel default, last success, next due time, failure count, ETag, and last observed target. The main app can read/write preferences through a narrow shared settings model, but it does not install anything.
- Persists `HighestAuthenticatedVersion` per channel. Reject a subsequently observed signed feed below that value as replay, and every target `<=` installed. A first-time client cannot detect withholding of a newer release, but it never installs an unsigned or older-than-known target.
- Fast path: when a successful check is not due (default 24 hours), perform no DNS/network work and immediately start `networker.exe` from the current version directory.
- Due path: enforce one hard two-second wall-clock budget over DNS/connect/headers/feed verification, plus ETag, fixed repository, selected channel, and strict release validation. On timeout/offline/API/rate limit/invalid metadata, cancel, persist backoff, and immediately launch the current app. A confirmed package download is not subject to this metadata budget.
- Update path: show a minimal signed launcher window only after a valid newer release is known: `Updating Networker...`, progress, and `Continue using current version`. Download through Velopack; verify authenticated feed/package; write a recovery journal and preserve the prior full package; call native `Update.exe`, exit, apply while no Networker binary is running, and restart the launcher.
- After a successful restart, briefly show `Networker <version> installed.`, then open the app. Ordinary cached/no-update/offline paths display no window.
- Never enables `AllowVersionDowngrade`; reject equal, older, invalid, wrong-channel, wrong-architecture, and mismatched package ID/version.
- Logs bounded diagnostics to `%LOCALAPPDATA%\Networker\Logs\launcher.log`; no tokens, full response bodies, query strings, user secrets, or raw UI exceptions.

#### Component 3: `networker.exe`

- Remains the WinUI 3 application and never updates or replaces its own binaries.
- Remove update DI registrations and scheduler startup from `App.xaml.cs`.
- Remove update event handling from `MainWindow.xaml.cs`.
- Settings update surface becomes installed version, stable/advanced-preview preference, last check, `Check for updates on next launch`, release link/status, and diagnostics location. No Download/Install/Restart button calls package APIs.
- Normal close already saves the troubleshooting session (`MainWindow.xaml.cs:330-337`). Launcher pre-launch updates happen before the app starts, so no app shutdown is needed in the normal path. Repair/manual update paths must wait for/close the app explicitly.

#### Signed release source

Do not use `GithubSource` unmodified as the production trust boundary. Velopack's feed contains package SHA-256 and is useful for integrity, but a malicious replacement of package plus feed in the same GitHub channel would pass checksum validation.

- Implement a custom Velopack `IUpdateSource` in a new update-policy library. It can reuse transport concepts from `Networker.Core\Updates\GitHubReleaseClient.cs` but must consume only fixed GitHub Release assets.
- Stable release assets contain a Velopack JSON feed (for example `releases.win-x64.json`) and a detached signature (`releases.win-x64.json.sig`). The signature covers exact UTF-8 feed bytes. Feed entries bind package ID, SemVer, channel, filename, byte size, SHA-256, and release tag.
- Sign exact UTF-8 feed bytes with **ECDSA P-256/SHA-256** using built-in .NET `ECDsa`; emit a detached base64 signature and key ID. Production signing calls a non-exportable cloud KMS/HSM key; pin current/next SubjectPublicKeyInfo public keys in the launcher. Prototype tests may use ephemeral/exportable PKCS#8 keys. This avoids adding a crypto library or inventing a package-signing scheme.
- The source downloads feed and signature from the same versioned release, but accepts them only after pinned-key verification. HTTPS still protects transport/privacy; the signature supplies authenticity independent of GitHub/CDN.
- Download selected `.nupkg` only from exact HTTPS `github.com`/`objects.githubusercontent.com` release URLs, with bounded redirects, size, and timeouts. Velopack then verifies SHA-256/size and applies its extraction safeguards.
- Keep staging under current-user update/package directories with user-only ACLs; reject reparse-point destinations and never derive local paths from release metadata. Pin Velopack and include traversal/symlink package fixtures in security tests instead of adding a second custom extractor.
- Authenticode-verify `Networker-Setup.exe`, `Networker.Launcher.exe`, `networker.exe`, `Update.exe`, and version package contents during CI and installation/update validation. Runtime package authenticity is rooted in signed feed + hash; Authenticode is defense in depth and Windows publisher trust.
- Key rotation: ship launcher version N with current and next public keys; release N+1 may switch signing key only after the new key is deployed. Revocation removes a key in a launcher signed by a still-trusted key. Loss of all trusted update keys requires a manually downloaded publicly Authenticode-signed Setup repair.

#### Version source of truth

- The **annotated Git tag** remains the release authorization (`vMAJOR.MINOR.PATCH`; optional `vMAJOR.MINOR.PATCH-preview.N`). `NetworkerVersionPolicy` strictly validates it.
- `Directory.Build.props` remains the checked-in local/dev default, but release workflow computes one `SEMVER` once from the tag and passes `-p:NetworkerVersion`, `Version`, `InformationalVersion`, and numeric `FileVersion` consistently to app, launcher, shared libraries, and package generation.
- Remove MSIX `65535` mapping from the live release contract. It remains only in historical migration code/tests until MSIX support is retired. Velopack/package/feed use actual semantic version.
- Installer metadata, Velopack package, signed feed, GitHub tag/release title, launcher installed version, and Settings/About display must all equal normalized SemVer. CI extracts each independently and fails on drift.
- Stable users accept only non-draft, non-prerelease releases whose tag is stable and whose signed feed channel is `win-x64`. Preview remains an advanced opt-in and uses `preview-win-x64`. Invalid tags/releases are ignored and logged. Automatic downgrades are never allowed, including a switch from preview to stable; users on a numerically newer preview wait for a newer stable or use explicit repair UI.

#### Release artifacts

The typical user downloads only:

```text
Networker-Setup.exe
```

The same GitHub Release also carries updater infrastructure assets, not intended for manual use:

```text
Networker-{semver}-win-x64-full.nupkg
Networker-{semver}-win-x64-delta.nupkg   # when a prior compatible version exists
releases.win-x64.json
releases.win-x64.json.sig
SHA256SUMS.txt                           # diagnostics/reproducibility, not the trust root
```

Preview releases use the corresponding pinned-vpk `preview-win-x64` feed filename. Exact filenames must follow pinned `vpk` output rather than renaming files Velopack expects; freeze both channels in `NetworkerVersionPolicy` after the prototype.

#### Lifecycle

```text
Fresh install
User -> Networker-Setup.exe (Authenticode verified by Windows)
     -> detect/migrate old MSIX if present
     -> Velopack per-user install + shortcuts/uninstall registration
     -> Networker.Launcher.exe
     -> networker.exe

Normal cached launch
Shortcut -> Networker.Launcher.exe
         -> check cache: not due
         -> networker.exe immediately

Due, offline/no update
Shortcut -> Launcher -> authenticated release check (short timeout/ETag)
                    -> failure or current
                    -> persist cadence/backoff
                    -> networker.exe

Update available
Shortcut -> Launcher -> verify signed feed -> newer stable package selected
                    -> download .partial -> size/SHA-256/AuthentiCode validation
                    -> launch independent Velopack Update.exe and exit
Update.exe -> wait for launcher/app exit -> apply staged version under recovery journal
           -> on success restart Launcher; on failure restore prior authenticated package
Launcher -> launch-health state -> networker.exe -> healthy marker
```

### Failure and recovery policy

| Failure | Required behavior |
|---|---|
| DNS/offline/GitHub unavailable/API 5xx | Abort check within short budget, update failure backoff, log concise category, launch current app. No ordinary-user dialog unless a manual check was requested. |
| Rate limit | Honor `Retry-After`/`X-RateLimit-Reset`, use ETag/cache, do not retry-loop, launch current app. |
| Interrupted download/user cancellation | Keep current install untouched. Use `.partial`; delete or resume only after metadata revalidation. Offer `Continue using Networker`. |
| Disk space insufficient | Preflight package size plus extraction/headroom; do not begin apply. Delete stale temp/delta artifacts, retain current app, show concise message. |
| Invalid/unsigned feed | Security failure: never use cached unverified bytes, never download/apply target, retain current app, prominently log key ID/reason without raw payload. |
| Hash/size/signature/publisher mismatch | Delete quarantined staged file, retain current version, launch current app. Do not silently fall back to an unverified package. |
| Updater/launcher terminated | Velopack lock and `.partial` state prevent concurrent/half-final downloads. Next launch cleans or safely resumes. Current install remains selected. |
| Shutdown/rename denial during apply | Treat as a Phase 2 production gate. Journal old/new versions and retain a copy of the prior full package outside Velopack cleanup. On next launch repair or explicitly re-apply the old package. If fault injection cannot prove this, reject Velopack and use the A/B fallback described below. |
| New app fails launch/health handshake | Pass a random health token to `networker.exe`; mark healthy only after the WinUI root loads. After two consecutive pre-health failures, explicitly re-apply the retained prior package and quarantine the target. Never rollback for a normal crash after health confirmation. Prototype must prove controlled downgrade/re-apply works. |
| Installer migration fails | Leave MSIX installed and usable, leave validated export for retry, remove only incomplete new install. Never delete old package data first. |
| Uninstall | Remove application binaries, shortcuts, and registration. Default to preserving `%LOCALAPPDATA%\Networker` user data; provide explicit separate `Remove my Networker data` action only if product UI supports it. |

### MSIX migration plan

The currently installed MSIX identity is `Name=12266223-d1a1-43c3-aca2-59c9ae71cd23`; determine and match its actual package family name at runtime rather than hardcoding a guessed publisher hash.

1. After Velopack Setup launches `Networker.Launcher.exe` with its first-run marker, the launcher checks current-user `PackageManager` for the exact package name and validates publisher/architecture. If the legacy app is running, ask once to close it; do not force termination until the user approves. Velopack Setup itself has no custom pre-install migration UI.
2. Acquire migration lock and first try `Windows.Management.Core.ApplicationDataManager.CreateForPackageFamily(packageFamilyName)` to access the existing package's `ApplicationData` store. Microsoft documents the API for a specified package family, but unpackaged caller authorization must be proven on supported Windows builds.
3. If direct access is denied, use a final bridge MSIX release: add an explicit export activation/command to the packaged app, have Setup launch it, and wait for a signed/versioned export. Do not parse proprietary `settings.dat`, crawl package internals, or uninstall before export.
4. Export **known settings keys only**: `OllamaEndpoint`, `OllamaApiKey`, `SelectedModel`, `ThemeMode`, `SelectedProvider`, `NetworkConfigDirectory`, `DefaultVendor`, `SelectedToolKey`, and preview/automatic-check preference. Protect the staging payload with current-user DPAPI and user-only ACLs because `OllamaApiKey` is secret. Never log values. Do not copy obsolete check timestamps/backoff/cache.
5. Copy known LocalFolder files (`GlobalSystemPrompt.txt`, `GlobalCustomInstructions.txt`, `troubleshooting-workspace.json`, and audited `.env` only if present and explicitly approved). Do not activate old MSIX updater cache, staged packages, or logs.
6. Vault/templates normally already reside in `%LOCALAPPDATA%\Networker` or configured `NetworkConfigDirectory`; validate but do not overwrite newer files. Preserve custom paths.
7. Write export under `%LOCALAPPDATA%\Networker\Migration\msix-{timestamp}\` via `.partial` and atomic rename. Record source package/version and file SHA-256 without logging content.
8. Install `Networker.Desktop` without removing MSIX. Import settings atomically. Conflict rule: retain an existing non-empty unpackaged destination, otherwise import MSIX; never silently replace newer data. The existing unpackaged store writes settings JSON, including the Ollama key; hardening that store to DPAPI/credential storage is recommended in Phase 1 but must be a tested app-settings migration, not an installer-only format change.
9. Launch new app in `--migration-verify` mode. It loads settings/workspace and writes a health marker without changing source data. Only on success remove old MSIX and its obsolete shortcuts. On failure remove/disable incomplete new install and keep MSIX.
10. Keep the protected migration backup for at least one successful run and 30 days. Do not automatically remove the old self-signed certificate; certificate-store cleanup may require admin and affect other software.

New users skip all migration logic and receive a normal per-user Setup. During transition, publish one final MSIX release whose in-app update UI links to `Networker-Setup.exe` and explains the one-time guided migration; do not attempt to use MSIX deployment to transform package type.

### Release pipeline

Retain tag-triggered draft-first publishing and the protected `production-release` environment, but replace MSIX-specific steps:

```text
annotated vX.Y.Z tag
 -> strict tag/SemVer validation
 -> restore + all tests + Debug build
 -> publish unpackaged self-contained app and launcher (same SEMVER)
 -> sign EXEs/DLLs with publicly trusted Authenticode cert + RFC 3161 timestamp
 -> vpk pack: Setup + full package + optional delta + Velopack feed
 -> sign Setup/Update.exe/package artifacts where supported
 -> generate exact signed release feed + SHA256SUMS
 -> offline validation: versions, signatures, feed signature, hashes, clean install/upgrade smoke
 -> create draft GitHub Release
 -> upload all required assets
 -> re-download and verify draft assets
 -> publish only after the complete draft asset set verifies
```

- Prefer a cloud/HSM-backed code-signing provider with OIDC or short-lived credentials. If PFX secrets are temporarily necessary, use protected environment secrets, materialize only in runner temp, mask password, and delete in `always()`.
- Use RFC 3161 timestamping so signatures remain valid after certificate expiry. Pin expected certificate subject and optionally leaf/public-key identity in CI; alert on unexpected rotation.
- Keep the **feed-signing key non-exportable in a cloud KMS/HSM** with ECDSA P-256 support and narrowly scoped OIDC/reviewer policy, separate from Authenticode credentials and `GITHUB_TOKEN`. GitHub environment approval is required; only public SubjectPublicKeyInfo is source-controlled/pinned. An exportable PKCS#8 GitHub secret is acceptable for isolated prototype fixtures, not production.
- Release workflow must fail closed when either signing system is unavailable. Do not publish unsigned installable/update artifacts and do not create source-only releases under production version tags because GitHub `latest` could become unusable updater metadata.
- Generate delta only when the previous compatible stable/full package can be authenticated and downloaded. Failure to generate a delta does not block a release; full package is mandatory.
- Add concurrency by tag/channel and prevent overwriting existing tags/releases/assets. Keep drafts invisible to clients.
- CI (`.github\workflows\ci.yml`) adds launcher/update-policy tests and an unpackaged Release publish; release-only VM install tests may run in the protected workflow.

## Changes

Implement in small reviewable phases. Do not remove MSIX production behavior until Phase 7 gates pass.

### Phase 1: Freeze shared version, state, and trust contracts

Create `C:\Users\Kenny\source\repos\networker\Networker.Update.Contracts\Networker.Update.Contracts.csproj` targeting `net8.0` with nullable/implicit usings and `NuGet.Versioning`. It must not reference Velopack, WinUI, Windows App SDK, or `Networker.Core`; both launcher and main app may safely use it. It owns:

- `Versioning\NetworkerVersionPolicy.cs`: move/refactor strict tag grammar and repository constants from `Networker.Core\Updates\NetworkerVersionPolicy.cs`. Preserve accepted forms `vX.Y.Z` and `vX.Y.Z-preview.N`, stable default, normalized comparison, and invalid/downgrade rejection. Remove MSIX version/asset methods from the active policy; move them to a migration-only `LegacyMsixVersionPolicy.cs` until cutover.
- `State\LauncherState.cs` and `LauncherStateStore.cs`: schema-versioned `%LOCALAPPDATA%\Networker\Updates\launcher-state.json`, interprocess lock, atomic temp/replace, corruption fallback, channel, last/next check, failure count, ETag, manual-check flag, first-run/desktop choice, last target, highest authenticated version, and recovery journal reference. Base the atomic/error-tolerant pattern on current `Networker.Core\Updates\UpdateCacheFile.cs:34-76`.
- `Scheduling\UpdateSchedulePolicy.cs`: move the current 24-hour success interval and `15m, 1h, 6h, 24h` failure backoff from `UpdateSchedulerPolicy.cs`; add the user-decided two-second metadata deadline as a constant.
- `Migration\MigrationContracts.cs`: schema and known settings/file allowlist, but no Windows access implementation yet.

Create `C:\Users\Kenny\source\repos\networker\Networker.Update\Networker.Update.csproj` targeting `net8.0`, referencing `Networker.Update.Contracts`, with pinned `Velopack` 1.2.0. This is the independent update engine and owns:

- `Security\ReleaseFeedVerifier.cs`: import pinned ECDSA P-256 public SubjectPublicKeyInfo, select by key ID, verify SHA-256 detached signatures over exact feed bytes, reject unknown/duplicate keys and malformed/base64 signatures. No file extraction.
- `Releases\SignedGitHubReleaseSource.cs`: custom Velopack `IUpdateSource`; fixed `NormalDudeBro/networker`, stable `/releases/latest`, preview paged releases endpoint, ETag/rate-limit handling, exact asset/URL/host validation, signed feed before deserialization, channel/tag/prerelease consistency, size caps, redirects, and cancellation. Reuse behavior, not MSIX DTO shape, from `Networker.Core\Updates\GitHubReleaseClient.cs:29-112,151-249`.
- `Diagnostics\UpdateLog.cs`: bounded/rotating launcher log with existing no-secret/no-response-body convention.

Create `C:\Users\Kenny\source\repos\networker\Networker.Update.Tests\Networker.Update.Tests.csproj` targeting `net8.0` with xUnit and references to both new libraries. Move reusable version/scheduler/cache tests from `Networker.Core.Tests\Updates\`; add signed-feed fixtures generated with ephemeral test keys. All network tests use fake `HttpMessageHandler`/`IUpdateSource`, never live GitHub.

Modify:

- `networker.sln`: add `Networker.Update.Contracts`, `Networker.Update`, and `Networker.Update.Tests` for Any CPU library/test builds and x64 app/launcher configurations.
- `Directory.Build.props`: keep `NetworkerVersion=1.0.0-dev` for local builds. Release passes one normalized tag SemVer to all projects. Keep `AssemblyVersion` stable for compatibility; set exact SemVer in `Version`, `PackageVersion`, and `InformationalVersion`; derive numeric `FileVersion=MAJOR.MINOR.PATCH.0` because Windows file version cannot represent prerelease labels. Update UI reads informational/package SemVer, never file version.
- `scripts\Prepare-Release.ps1`: in this phase, remove certificate/manifest mutation and MSIX asset mapping from the new-channel path. Emit normalized `tag`, `semver`, `channel` (`win-x64` or `preview-win-x64`), `prerelease`, `file_version`, `pack_id=Networker.Desktop`, and expected feed names. Keep a clearly named legacy MSIX switch/script until migration release retirement. Continue cross-checking strict tag vectors against the built policy assembly to prevent PowerShell/C# drift.

Phase 1 verification: equal/older/newer/invalid/prerelease matrices, feed canonical-byte signature tests, key rotation tests, corrupt state recovery, atomic write interruption, ETag/304-without-cache retry, rate-limit reset, and two-second cancellation under a fake hanging handler.

### Phase 2: Velopack/WinUI feasibility and recovery prototype

Create `C:\Users\Kenny\source\repos\networker\Networker.Launcher\Networker.Launcher.csproj` as `WinExe`, `net8.0-windows10.0.19041.0`, x64, self-contained **single-file**, non-trimmed for prototype, with `UseWindowsForms=true`, references to `Networker.Update` and `Networker.Update.Contracts`. Velopack arrives only through `Networker.Update`. Single-file avoids output collisions when launcher and self-contained WinUI publishes are combined; measure bundle size/extraction/startup. WinForms is used only after update/migration needs UI; cached fast path creates no form. NativeAOT is only a later optimization if Velopack compatibility is proven.

Create prototype files:

- `Networker.Launcher\Program.cs`: `[STAThread]`, call `VelopackApp.Build().Run()` before WinForms initialization, locate current installed version, invoke coordinator, launch `networker.exe` with safely quoted original arguments.
- `Networker.Launcher\LauncherCoordinator.cs`: cached fast path, two-second due check, no-update/offline fallthrough, download/apply handoff to separate `Update.exe`.
- `Networker.Launcher\UpdateProgressForm.cs`: only `Updating Networker...`, version/progress, and `Continue using current version`; no raw exceptions.
- `Networker.Launcher\RecoveryJournal.cs`: old/new version, package hashes, apply phase, attempt/failure count, health token, atomic persistence outside `%LOCALAPPDATA%\Networker.Desktop` so a damaged/uninstalled package cannot erase recovery evidence.

Prototype package command must pin:

```text
packId: Networker.Desktop
entry executable: Networker.Launcher.exe
title: Networker
install location: PerUser
shortcuts: StartMenuRoot only
runtime: win-x64
portable: disabled
MSI: disabled
```

Use `Properties\PublishProfiles\win-x64.pubxml` (`WindowsPackageType=None`, self-contained Windows App SDK) to publish the app, copy launcher output into one staging directory, then run pinned `vpk` against it. Do not use `%LOCALAPPDATA%\Networker` as pack ID/root.

Hard go/no-go tests on disposable Windows 10 22H2 and current Windows 11 VMs:

1. Clean Setup installs without UAC; Start Menu launches launcher then unpackaged WinUI app.
2. Self-contained app starts with no separately installed .NET/Windows App SDK runtime.
3. Full and delta update apply while launcher/app are closed.
4. Existing running `networker.exe`: launcher detects a named app mutex, does not apply, and offers current launch/defer rather than force-closing unsaved work.
5. Kill download, kill launcher, kill `Update.exe` before old rename, kill after old rename, simulate denied second rename, disk-full extraction, reboot each phase, and retry.
6. Recovery journal restores/re-applies the old authenticated full package whenever the new version is not healthy. Verify the old app and user data remain runnable.
7. New app crash before health marker twice triggers rollback; crash after marker does not.
8. Repair/reinstall and uninstall do not touch `%LOCALAPPDATA%\Networker`.

**Gate:** Velopack advances only if every destructive fault leaves either old or new Networker launchable automatically. If its updater cannot be wrapped safely, stop and prototype the fallback: Inno Setup for one-file per-user install plus a Networker-owned A/B root (`app-a`, `app-b`, atomic small active-slot file, launcher never overwritten in-place). Do not ship around the second-rename caveat.

### Phase 3: Complete independent launcher

After Phase 2 passes, finish `Networker.Launcher`:

- `Program.cs`: process Velopack hooks first; first-run/migration branch; recover incomplete journal; serialize launcher/update operations with a per-user named mutex; preserve command-line arguments; never block current launch after an update exception.
- `LauncherCoordinator.cs`: state machine `Recovering, CachedLaunch, Checking, Downloading, Verifying, WaitingForExit, Applying, Launching, FailedSafe`; map exceptions to concise messages and logs.
- `MainAppProcess.cs`: start only the `networker.exe` sibling under the current authenticated installation; never accept executable paths from release JSON/user input. Detect the app-owned named mutex. Wait rather than force-close for manual repair/update; ordinary second launch defers update.
- `LaunchHealthMonitor.cs`: create cryptographically random token, pass `--networker-health-token <token>`, wait a bounded 15 seconds in the background for `%LOCALAPPDATA%\Networker\Updates\health\<token>.ok`, then clear journal. Two pre-health failures quarantine the target and apply prior package.
- `UpdateProgressForm.cs`: show UI only for actual download/apply/recovery or explicit manual check. Close/failure always has `Continue using current version` when a healthy current version exists.
- `DesktopShortcutService.cs` and `FirstRunForm.cs`: optional unchecked desktop shortcut. Target Velopack's stable root launcher/stub, icon from signed app, current-user desktop only. Persist choice; respect later user deletion.
- `Properties\app.manifest`: `asInvoker`, Windows 10 compatibility, per-monitor DPI. Never request elevation.

The launcher must not reference WinUI, `networker.csproj`, LLM/network tools, or `Networker.Core`; enforce with an architecture test. Conversely, `networker.csproj` may reference only `Networker.Update.Contracts`, never `Networker.Update`, `Networker.Launcher`, or Velopack.

### Phase 4: Release pipeline and artifact trust

Add `.config\dotnet-tools.json` pinning stable `vpk` 1.2.0. Upgrade only in a dedicated PR that reruns Phase 2 recovery tests.

Create:

- `scripts\New-SignedReleaseFeed.ps1`: consume vpk's exact feed bytes, validate package IDs/versions/channels/files/hashes, sign bytes with ECDSA P-256 private key, and emit `.sig` containing schema/key ID/signature. No JSON reserialization after signing.
- `scripts\Test-ReleaseArtifacts.ps1`: verify required set, no extras with ambiguous contract names, Setup/PE Authenticode and RFC 3161 timestamp, signer expectation, feed signature using source-controlled public key, package size/SHA-256, package content paths, exact version equality, and absence of PFX/private keys/debug secrets.
- `docs\release-signing.md`: Authenticode provider operation, update-key generation/rotation/revocation, emergency Setup recovery, environment approvals, and timestamp policy.

Rewrite `.github\workflows\release.yml` in a separate phase while retaining draft-first/immutable publishing:

- Trigger only strict version tags; use `concurrency` per channel/tag and protected `production-release` environment.
- Restore/test both test projects; publish unpackaged app and launcher with one `SEMVER`; combine outputs and reject duplicate filenames.
- Sign all PE files before packaging through a publicly trusted Authenticode service, preferably cloud/HSM with OIDC. Use SHA-256 and RFC 3161 SHA-256 timestamp. Then let vpk sign generated Setup/Update helpers through its signing hook/provider.
- Download the last authenticated compatible full package to produce a delta; full package is mandatory, delta optional.
- Generate vpk Setup/full/delta/feed, sign feed with separate ECDSA key, generate diagnostic `SHA256SUMS.txt`.
- Run `Test-ReleaseArtifacts.ps1`, silent clean install/launch/update/uninstall smoke on Windows, then create a draft, upload all assets, re-download and verify, and only then publish.
- Missing Authenticode or feed key fails the release. Remove current source-only fallback for production tags because it can poison GitHub `latest` and is not an installable release.
- Stable releases become latest; preview tags are GitHub prereleases and use separate `preview-win-x64` feed/channel.

Modify `.github\workflows\ci.yml` to restore tools, run both test projects, build launcher, publish unpackaged x64 app, run architecture checks, and run unsigned local vpk packaging validation. CI does not contact live update services.

Expected public assets (use actual pinned-vpk filenames, frozen by tests):

```text
Networker-Setup.exe
Networker-<semver>-win-x64-full.nupkg
Networker-<semver>-win-x64-delta.nupkg  # optional
releases.win-x64.json                   # stable; exact vpk name frozen by prototype
releases.win-x64.json.sig
# Preview releases use the corresponding preview-win-x64 feed + signature.
SHA256SUMS.txt
```

### Phase 5: Remove update mechanics from the WinUI app

Modify `networker.csproj`:

- Reference `Networker.Update.Contracts` only for shared read-only state/preferences/version display. Do not reference `Networker.Update` or Velopack.
- Keep `WindowsPackageType=None`, self-contained x64 and Windows App SDK self-contained for new release publish.
- Add release icon/version metadata needed by launcher/Velopack.
- Keep MSIX tooling conditionally available only for the bridge release until migration cutover; do not mix it into normal new-channel publish.

Modify `App.xaml.cs` current implementation at lines 80-110 and 123-134:

- Remove update `HttpClient`, downloader/verifier/installer/coordinator/scheduler/restart DI registrations.
- Remove post-window `CleanupConfirmedStaged()` and `UpdateScheduler.Start()`.
- Parse health token safely and register a lightweight `LaunchHealthService`.

Create `Services\LaunchHealthService.cs`: validate token character/length, confine path under the health directory, write marker atomically only after `MainWindow` root Loaded and essential persistence/DI initialization succeeds. Hold an app-running named mutex for launcher update deferral.

Modify `MainWindow.xaml.cs`:

- Remove `_updateCoordinator`, subscriptions, toast notification, scheduler stop, and `UpdateCoordinator_StateChanged` (`:31,51-57,296-300,330-337`).
- On root Loaded, signal launch health.

Modify `Views\SettingsPage.xaml:220-289` and `Views\SettingsPage.xaml.cs:263-485`:

- Retain installed semantic version, automatic checks, advanced preview opt-in, last check/status, and a manual action named `Check on next launch` (write launcher state then restart through launcher on user request).
- Remove in-app download/progress/install/cancel/restart/later state and all `UpdateCoordinator` dependencies.
- Make preview warning explicit: preview can be unstable; returning to stable never downgrades automatically.
- Version comes from shared `AssemblyInformationalVersion`/Velopack package metadata and must match.

Delete only after bridge release/cutover validation:

- `Services\Updates\*.cs`
- MSIX-specific files in `Networker.Core\Updates\` and corresponding tests (`UpdatePackageDownloader`, verifier, selector, coordinator, checksum models/contracts).
- `scripts\New-AppInstaller.ps1`
- Active MSIX packaging blocks in `networker.csproj`, `Package.appxmanifest`, and MSIX-only release docs/workflow branches. Preserve a tagged historical branch/release, not compatibility code in perpetuity.

Update `README.md:71-99,128-142` and replace `docs\UPDATES.md` with the Setup/launcher/signed-feed/operator contract. Explain that ordinary users download only Setup; do not expose certificate commands.

### Phase 6: Existing MSIX guided migration

Create Windows implementation in `Networker.Launcher\Migration\`:

- `MsixDetector.cs`: current-user `PackageManager` query by exact legacy Name `12266223-d1a1-43c3-aca2-59c9ae71cd23`; validate expected publisher `CN=Kenny`, x64, installed location, and derive actual package family dynamically.
- `MsixDataExporter.cs`: direct `ApplicationDataManager.CreateForPackageFamily` attempt, allowlisted settings/files, DPAPI current-user protected export, user-only ACL, hashes and atomic completion marker.
- `MigrationCoordinator.cs`: detect, ask user once to close Networker, export, import, run `--migration-verify`, remove old package only after health, remove duplicate legacy shortcut, retain backup.
- `MigrationForm.cs`: plain steps `Preparing your Networker settings`, `Installing Networker`, `Finishing setup`; retry/continue-old-version choices; no package/certificate terminology.

If direct cross-package access fails Phase 2 VM tests, prepare final bridge MSIX before public cutover:

- Add `Services\MigrationExportService.cs` to packaged app and a controlled command/activation handled in `App.xaml.cs` before normal UI.
- Export the same DPAPI/schema payload to the agreed migration location and exit.
- Publish through the existing trusted MSIX workflow as the last MSIX update; the new launcher invokes this bridge. Existing users who never received the bridge get a concise instruction to launch/update old Networker once, not manual file/certificate work.

Migration must include `OllamaApiKey`; losing credentials is not acceptable. Never print it, place it unprotected in migration staging, or include it in diagnostic archives. Existing final unpackaged `settings.json` plaintext storage should be separately hardened with a versioned DPAPI migration during Phase 1/5 if tests show no compatibility risk.

### Phase 7: Test matrix and production canary

Add deterministic tests under `Networker.Update.Tests`:

- Version: stable/preview/older/equal/newer/invalid/overflow/build metadata/downgrade/channel switch.
- GitHub: draft, prerelease mismatch, missing/duplicate assets, invalid URL/redirect, ETag 304, pagination, rate limit, timeout, API failures, malformed JSON.
- Trust: valid feed, byte mutation, wrong/unknown/revoked key, invalid signature encoding, package hash/size/name/id/version mismatch, replayed older signed feed, key rotation current/next.
- State/scheduler: first launch, cached launch, due, manual due, success cadence, backoff, corrupt/truncated state, simultaneous writers.
- Coordinator: no update, full/delta update, multiple jumps, cancellation, app-running deferral, package failure, recovery journal for every phase, health rollback.
- Migration: each known setting including `OllamaApiKey`, each known file, corrupt/partial export, ACL/DPAPI wrong user, destination conflict, custom config path, old package absent, uninstall failure, idempotent retry.

Installer/updater VM matrix (snapshots, no live production dependency; serve signed fixtures from a controllable test HTTP server or private test release repository):

- Fresh interactive and silent install; optional desktop unchecked/checked; Start Menu; Apps & Features; no admin; clean uninstall preserving data.
- Reinstall/repair; upgrade one and multiple versions; full fallback when delta corrupt/missing; preview isolation.
- Windows 10 minimum supported build and current Windows 11; x64 only; missing runtimes; low disk; locked files; standard user.
- Offline, DNS failure, TLS failure, two-second timeout, API 403/429/500, interrupted downloads, corrupt package, invalid Authenticode/feed signature.
- Kill/reboot at every download/apply rename/journal/health phase. Previous app must remain or be automatically restored.
- MSIX migration with default/custom data, settings/prompts/workspace/vault/templates/API key, duplicate shortcuts, migration retry and rollback.
- Application startup, normal shutdown/session save, health marker, settings persistence, app behavior after update and rollback.

Performance gates measured on warm/cold representative hardware:

- Cached launcher median overhead target <=100 ms and p95 <=250 ms before spawning main app.
- Due offline check hard ceiling <=2.25 s including cancellation/cleanup.
- No GitHub/network request before 24-hour due time unless manual flag/channel change.
- Bounded launcher log/state and no lingering launcher after health decision.

Canary sequence:

1. Internal `preview` feed, unsigned only on isolated test VMs; never public production.
2. Publicly signed `vNEXT-preview.1` to opt-in testers; validate Setup, update to preview.2, rollback, migration.
3. Publish final bridge MSIX pointing users to one-file Setup.
4. Publish signed stable Setup channel to a small manual cohort; monitor sanitized failure categories.
5. Make stable GitHub Release latest only after install/update/migration evidence is recorded.
6. Keep prior MSIX release and prior authenticated full package available through the rollback window; retire MSIX workflow after adoption/support window, not immediately.

## Dead Ends / Constraints

- Do not implement an updater inside `networker.exe`; it cannot safely replace its own loaded EXE/DLL set.
- Do not retain a fresh-install flow that requires users to import a certificate manually.
- Do not treat SHA-256 fetched from the same GitHub Release as sufficient authenticity. Production installer/updater binaries need Authenticode signing with a publicly trusted code-signing certificate or an equivalent strong trust model.
- Do not track source commits or download arbitrary repository files. Only immutable, versioned GitHub Releases may drive production updates.
- Do not delete the working MSIX channel until the replacement installer, migration, rollback, and update paths have passed end-to-end validation.
- Do not move user data into the installation directory. Installer upgrades/uninstalls must not own or delete settings, vaults, templates, troubleshooting workspaces, or update logs.
- Do not implement all phases in one change; versioning/feed contracts and installer prototype must stabilize before launcher integration and MSIX migration.
- Do not use Velopack package ID `Networker`; its uninstaller owns `%LOCALAPPDATA%\<PackId>`, which would collide with existing `%LOCALAPPDATA%\Networker` user data. Freeze `Networker.Desktop` unless a tested explicit non-colliding root is chosen before first release.
- Do not claim Velopack is transactionally safe under every interruption based on marketing. Its 1.2.0 Windows source has a second-rename repair gap. Phase 2 fault tests are mandatory and can reject the recommendation.
- Do not use Velopack `GithubSource` directly as production trust. Its feed checksum is integrity only when feed and package share the same mutable authority.
- Do not force-close a running Networker update. Detect the app mutex, defer/wait with user consent, and keep current app usable.
- Do not auto-downgrade preview users on channel switch. SemVer ordering and `AllowVersionDowngrade=false` are invariant.
- Do not assume `ApplicationDataManager.CreateForPackageFamily` works from the unpackaged launcher on every supported Windows version. Test it; retain the bridge-MSIX fallback.
- Do not parse MSIX `settings.dat` or copy package internals by undocumented paths.
- Do not place `OllamaApiKey` in unprotected migration JSON/logs. Protect migration payload with current-user DPAPI and ACL.
- Do not create source-only production releases under version tags; `/releases/latest` must always be an authenticated installable stable release.
- Do not auto-remove the legacy self-signed certificate from user/machine stores.
- Do not add per-machine scope initially. It introduces UAC into updates and doubles permission/recovery testing.

## Verification

Run after each phase (exact new project names are fixed above):

```powershell
dotnet restore networker.sln
dotnet test Networker.Core.Tests\Networker.Core.Tests.csproj -c Debug --no-restore
dotnet test Networker.Update.Tests\Networker.Update.Tests.csproj -c Debug --no-restore
dotnet build networker.csproj -c Debug -p:Platform=x64 --no-restore
dotnet build Networker.Launcher\Networker.Launcher.csproj -c Debug -p:Platform=x64 --no-restore
dotnet publish networker.csproj -c Release -p:Platform=x64 --no-restore
dotnet tool restore
```

CI/package validation additionally runs `scripts\Prepare-Release.ps1 -Tag <fixture> -SelfTest`, local unsigned `vpk` packaging, and `scripts\Test-ReleaseArtifacts.ps1` against test keys. Production workflow runs `signtool verify /pa /all /v` on Setup and every PE extracted from the package, verifies RFC 3161 timestamps, detached feed signature, feed/package SemVer equality, exact package ID `Networker.Desktop`, x64 architecture, and re-downloaded draft hashes.

Release acceptance is binary:

- A standard user downloads only `Networker-Setup.exe`, installs without PowerShell/certificate/UAC, and launches from Start Menu.
- Cached launch has no network and meets latency target.
- Due offline launch opens current Networker after at most the two-second budget.
- A valid newer stable release updates through independent processes while `networker.exe` is not running, then launches the healthy new version.
- Invalid metadata/signatures/hashes/downgrades never alter current install.
- Every fault-injection checkpoint automatically leaves/restores a launchable version.
- Uninstall/repair/update never delete `%LOCALAPPDATA%\Networker` or configured data.
- Existing MSIX settings, API key, prompts, workspace, vault/templates, and custom path survive guided migration; failure retains old MSIX.
- Stable users never receive preview. Preview is opt-in and never silently downgrades.

## Dependencies

1. Product decisions and repository audit are complete: per-user/no-admin, public Authenticode, guided migration, Start Menu + optional unchecked desktop, stable + advanced preview, two-second due budget.
2. Phase 1 freezes `Networker.Desktop`, SemVer/feed/state/signature schemas and test keys before installer/launcher work.
3. Phase 2 is a serial blocker. Velopack WinUI compatibility and destructive fault recovery must pass before Phases 3-6. Failure switches architecture to Inno + A/B launcher and requires a plan revision.
4. Phase 3 launcher depends on Phase 1 contracts and Phase 2 recovery mechanics. It can proceed in parallel with Phase 4 workflow scaffolding only after vpk output names/options are frozen.
5. Phase 4 production signing requires acquisition/configuration of a publicly trusted Authenticode service and separate ECDSA feed key. No production release without both.
6. Phase 5 app integration depends on launcher state/health contracts. Keep current MSIX updater operational on the migration branch until Phase 7.
7. Phase 6 migration depends on direct API feasibility results. Build bridge MSIX before stable Setup if any supported OS denies access.
8. Phase 7 full VM/fault/security matrix blocks public stable release.
9. Publish bridge MSIX, then stable Setup/new channel; retire MSIX code/workflow only after rollback/support window and migration adoption evidence.
