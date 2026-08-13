<#
    New-AppInstaller.ps1

    Generates Networker-x64.appinstaller for a validated release. The MSIX URI
    is version-pinned under /releases/download/{tag}/... and the identity comes
    from the patched Package.appxmanifest.

    UpdateSettings is deliberately OMITTED: Networker performs its own in-app
    checks, and an OS-level automatic updater would be a second updater.

    Usage:
      powershell -File scripts/New-AppInstaller.ps1 -Tag v1.2.3 -PackageName 12266223-d1a1-43c3-aca2-59c9ae71cd23 -Publisher "CN=Networker, O=NormalDudeBro" -MsixVersion 1.2.3.65535 -MsixAssetName Networker-1.2.3-win-x64.msix -OutputPath artifacts\Networker-x64.appinstaller -SelfTest
#>
[CmdletBinding()]
param(
    [string]$Tag,

    [string]$PackageName,

    [string]$Publisher,

    [string]$MsixVersion,

    [string]$MsixAssetName,

    [string]$OutputPath,

    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AppInstallerAssetName = 'Networker-x64.appinstaller'
$RepositoryPath = 'NormalDudeBro/networker'
$ReleasesDownloadBase = "https://github.com/$RepositoryPath/releases/download"

function New-AppInstallerContent {
    param(
        [string]$AppInstallerUrl,
        [string]$PackageNameValue,
        [string]$PublisherValue,
        [string]$MsixVersionValue,
        [string]$MsixUrl
    )

    return @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
  Uri="$AppInstallerUrl"
  Version="$MsixVersionValue"
  xmlns="http://schemas.microsoft.com/appx/appinstaller/2017/2">
  <MainPackage
    Name="$PackageNameValue"
    Publisher="$PublisherValue"
    Version="$MsixVersionValue"
    ProcessorArchitecture="x64"
    Uri="$MsixUrl" />
</AppInstaller>
"@
}

function Write-AppInstaller {
    param([string]$Path, [string]$Content)

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $utf8WithBom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8WithBom)
}

if ($SelfTest) {
    $tag = 'v1.2.3'
    $packageName = '12266223-d1a1-43c3-aca2-59c9ae71cd23'
    $publisher = 'CN=Networker, O=NormalDudeBro'
    $msixVersion = '1.2.3.65535'
    $msixAssetName = 'Networker-1.2.3-win-x64.msix'
    $downloadBase = "$ReleasesDownloadBase/$tag"
    $msixUrl = "$downloadBase/$msixAssetName"
    $appInstallerUrl = "$downloadBase/$AppInstallerAssetName"

    $content = New-AppInstallerContent -AppInstallerUrl $appInstallerUrl -PackageNameValue $packageName -PublisherValue $publisher -MsixVersionValue $msixVersion -MsixUrl $msixUrl

    $tempPath = Join-Path ([System.IO.Path]::GetTempPath()) 'networker-appinstaller-selftest.xml'
    Write-AppInstaller -Path $tempPath -Content $content
    try {
        [xml]$doc = Get-Content -Raw -Path $tempPath
        $appInstaller = $doc.DocumentElement
        if ($appInstaller.NamespaceURI -ne 'http://schemas.microsoft.com/appx/appinstaller/2017/2') {
            throw "SELFTEST FAILED: unexpected AppInstaller namespace $($appInstaller.NamespaceURI)"
        }
        if ($appInstaller.GetAttribute('Uri') -ne $appInstallerUrl) {
            throw 'SELFTEST FAILED: AppInstaller Uri mismatch'
        }
        if ($appInstaller.GetAttribute('Version') -ne $msixVersion) {
            throw 'SELFTEST FAILED: AppInstaller Version mismatch'
        }
        $main = $appInstaller.GetElementsByTagName('MainPackage').Item(0)
        if ($null -eq $main) { throw 'SELFTEST FAILED: MainPackage missing' }
        if ($main.GetAttribute('Name') -ne $packageName) { throw 'SELFTEST FAILED: MainPackage Name mismatch' }
        if ($main.GetAttribute('Publisher') -ne $publisher) { throw 'SELFTEST FAILED: MainPackage Publisher mismatch' }
        if ($main.GetAttribute('Version') -ne $msixVersion) { throw 'SELFTEST FAILED: MainPackage Version mismatch' }
        if ($main.GetAttribute('ProcessorArchitecture') -ne 'x64') { throw 'SELFTEST FAILED: MainPackage architecture mismatch' }
        if ($main.GetAttribute('Uri') -ne $msixUrl) { throw 'SELFTEST FAILED: MainPackage Uri mismatch' }
        if ($doc.GetElementsByTagName('UpdateSettings').Count -ne 0) {
            throw 'SELFTEST FAILED: UpdateSettings must be omitted (in-app updater owns checks)'
        }
    }
    finally {
        Remove-Item -Path $tempPath -Force -ErrorAction SilentlyContinue
    }

    Write-Host 'New-AppInstaller self-test passed.'
    exit 0
}

if ([string]::IsNullOrEmpty($Tag)) { throw 'Tag is required (e.g. v1.2.3 or v1.2.3-preview.1).' }
if ([string]::IsNullOrEmpty($PackageName)) { throw 'PackageName is required (manifest Identity Name).' }
if ([string]::IsNullOrEmpty($Publisher)) { throw 'Publisher is required (manifest Identity Publisher subject).' }
if ([string]::IsNullOrEmpty($MsixVersion)) { throw 'MsixVersion is required (four-part package version).' }
if ([string]::IsNullOrEmpty($MsixAssetName)) { throw 'MsixAssetName is required.' }
if ([string]::IsNullOrEmpty($OutputPath)) { throw 'OutputPath is required.' }

if (-not $MsixAssetName.EndsWith('.msix')) {
    throw "MsixAssetName must end with '.msix': $MsixAssetName"
}

$downloadBase = "$ReleasesDownloadBase/$Tag"
$msixUrl = "$downloadBase/$MsixAssetName"
$appInstallerUrl = "$downloadBase/$AppInstallerAssetName"

$content = New-AppInstallerContent -AppInstallerUrl $appInstallerUrl -PackageNameValue $PackageName -PublisherValue $Publisher -MsixVersionValue $MsixVersion -MsixUrl $msixUrl
Write-AppInstaller -Path $OutputPath -Content $content

Write-Host "Wrote $OutputPath"
Write-Host "AppInstaller URL: $appInstallerUrl"
Write-Host "MSIX URL: $msixUrl"
