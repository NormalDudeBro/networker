#Requires -Version 5.1
<#
.SYNOPSIS
  Downloads, verifies, and extracts the pinned official codex-app-server package.

.DESCRIPTION
  CI/local packaging only. Never used at application runtime.
  Pins must match Services/Codex/CodexAppServerDistribution.cs.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts\codex\win-x64',
    [string]$StagingDirectory = 'artifacts\codex\staging',
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Version = '0.149.0'
$ReleaseTag = 'rust-v0.149.0'
$PackageAsset = 'codex-app-server-package-x86_64-pc-windows-msvc.tar.gz'
$PackageSizeBytes = 116042307
$PackageSha256 = '580207baa5ecabb8e42fd734bdb774ffcd82709ccd60bff8fa812b1b83962e28'
$RequiredRelativePaths = @(
    'bin/codex-app-server.exe'
    'bin/codex-code-mode-host.exe'
    'codex-package.json'
    'codex-path/rg.exe'
    'codex-resources/codex-command-runner.exe'
    'codex-resources/codex-windows-sandbox-setup.exe'
)

$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$staging = [IO.Path]::GetFullPath((Join-Path $root $StagingDirectory))
$packageJsonPath = Join-Path $output 'codex-package.json'
$entryPoint = Join-Path $output 'bin\codex-app-server.exe'

function Get-FileSha256([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

function Test-PackageLayout([string]$Directory) {
    foreach ($relative in $RequiredRelativePaths) {
        $path = Join-Path $Directory ($relative -replace '/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Codex package is missing required path: $relative"
        }
    }

    $manifestPath = Join-Path $Directory 'codex-package.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.layoutVersion -ne 1) { throw "Unexpected layoutVersion: $($manifest.layoutVersion)" }
    if ("$($manifest.version)" -ne $Version) { throw "Unexpected package version: $($manifest.version)" }
    if ("$($manifest.target)" -ne 'x86_64-pc-windows-msvc') { throw "Unexpected target: $($manifest.target)" }
    if ("$($manifest.variant)" -ne 'codex-app-server') { throw "Unexpected variant: $($manifest.variant)" }
    if ("$($manifest.entrypoint)" -ne 'bin/codex-app-server.exe') { throw "Unexpected entrypoint: $($manifest.entrypoint)" }
    if ("$($manifest.resourcesDir)" -ne 'codex-resources') { throw "Unexpected resourcesDir: $($manifest.resourcesDir)" }
    if ("$($manifest.pathDir)" -ne 'codex-path') { throw "Unexpected pathDir: $($manifest.pathDir)" }
}

if (-not $Force -and (Test-Path -LiteralPath $entryPoint) -and (Test-Path -LiteralPath $packageJsonPath)) {
    try {
        Test-PackageLayout $output
        Write-Host "Codex app-server $Version already present at $output"
        return
    }
    catch {
        Write-Warning "Existing Codex layout invalid; re-acquiring. $_"
    }
}

$downloadUrl = "https://github.com/openai/codex/releases/download/$ReleaseTag/$PackageAsset"
$archivePath = Join-Path $staging $PackageAsset
$extractRoot = Join-Path $staging 'extract'

Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $staging -Force | Out-Null
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

Write-Host "Downloading $downloadUrl"
Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing

$actualSize = (Get-Item -LiteralPath $archivePath).Length
if ($actualSize -ne $PackageSizeBytes) {
    throw "Codex package size mismatch. Expected $PackageSizeBytes, got $actualSize."
}

$actualHash = Get-FileSha256 $archivePath
if ($actualHash -ne $PackageSha256) {
    throw "Codex package SHA-256 mismatch. Expected $PackageSha256, got $actualHash."
}

Write-Host "Extracting verified package"
# tar is available on Windows 10+ / GitHub windows-2022 runners.
& tar -xzf $archivePath -C $extractRoot
if ($LASTEXITCODE) { throw "tar extraction failed with exit code $LASTEXITCODE." }

# Package may extract flat or under a single top-level directory.
$candidate = $extractRoot
$children = @(Get-ChildItem -LiteralPath $extractRoot -Force)
if ($children.Count -eq 1 -and $children[0].PSIsContainer) {
    $nestedManifest = Join-Path $children[0].FullName 'codex-package.json'
    if (Test-Path -LiteralPath $nestedManifest) {
        $candidate = $children[0].FullName
    }
}

Test-PackageLayout $candidate

Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
Copy-Item -LiteralPath $candidate -Destination $output -Recurse -Force

# Optional Apache attribution files from the release, when present beside the archive.
foreach ($name in @('LICENSE', 'NOTICE')) {
    $source = Join-Path $staging $name
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $output $name) -Force
    }
}

Test-PackageLayout $output
Write-Host "Codex app-server $Version staged at $output"
Write-Host "Entrypoint: $entryPoint"
