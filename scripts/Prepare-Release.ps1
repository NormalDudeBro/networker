<#
    Prepare-Release.ps1

    Validates a Networker release tag against the frozen update contract,
    computes the SemVer/MSIX/asset mapping, optionally derives the signing
    certificate subject and patches Package.appxmanifest (Identity Version and
    Publisher only), and emits GitHub Actions outputs.

    The mapping logic in this script intentionally mirrors
    Networker.Core/Updates/NetworkerVersionPolicy.cs. Run -SelfTest to guard
    against drift; the release workflow additionally cross-checks against the
    built Core assembly when NETWORKER_CORE_ASSEMBLY is set.

    Usage:
      powershell -File scripts/Prepare-Release.ps1 -Tag v1.2.3 -SelfTest
      powershell -File scripts/Prepare-Release.ps1 -Tag v1.2.3 -CertificatePath cert.pfx -CertificatePassword $env:PFX_PASSWORD -ManifestPath Package.appxmanifest
#>
[CmdletBinding()]
param(
    [string]$Tag,

    [string]$ManifestPath,

    [string]$CertificatePath,

    [string]$CertificatePassword,

    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-Uint16Segment {
    param([string]$Segment)

    if ($Segment.Length -eq 0 -or $Segment.Length -gt 5) { return $false }
    if ($Segment -notmatch '^[0-9]+$') { return $false }
    if ($Segment.Length -gt 1 -and $Segment.StartsWith('0')) { return $false }

    $value = 0
    if (-not [int]::TryParse($Segment, [ref]$value)) { return $false }
    return $value -ge 0 -and $value -le 65535
}

function Test-StrictTag {
    param([string]$TagValue)

    if ($TagValue.Length -lt 2 -or $TagValue[0] -cne 'v') { return $false }
    $body = $TagValue.Substring(1)
    if ($body.Contains('+')) { return $false }

    $core = $body
    $release = $null
    $dash = $body.IndexOf('-')
    if ($dash -ge 0) {
        $core = $body.Substring(0, $dash)
        $release = $body.Substring($dash + 1)
    }

    $segments = $core.Split('.')
    if ($segments.Count -ne 3) { return $false }
    foreach ($segment in $segments) {
        if (-not (Test-Uint16Segment $segment)) { return $false }
    }

    if ($null -ne $release) {
        # Exactly "preview.N" with N in 1..65534.
        if (-not $release.StartsWith('preview.')) { return $false }
        $number = $release.Substring('preview.'.Length)
        if (-not (Test-Uint16Segment $number)) { return $false }
        $n = [int]$number
        if ($n -lt 1 -or $n -gt 65534) { return $false }
    }

    return $true
}

function Get-SemverFromTag {
    param([string]$TagValue)
    return $TagValue.Substring(1)
}

function Get-MsixVersion {
    param([string]$TagValue)

    $body = $TagValue.Substring(1)
    $core = $body
    $release = $null
    $dash = $body.IndexOf('-')
    if ($dash -ge 0) {
        $core = $body.Substring(0, $dash)
        $release = $body.Substring($dash + 1)
    }

    if ($null -eq $release) {
        return "$core.65535" # stable revision
    }

    $n = [int]$release.Substring('preview.'.Length)
    return "$core.$n"
}

function Get-MsixAssetName {
    param([string]$Semver)
    return "Networker-$Semver-win-x64.msix"
}

function Get-ChecksumAssetName {
    param([string]$MsixAssetNameValue)
    return "$MsixAssetNameValue.sha256"
}

function Get-CertificateSubject {
    param([string]$Path, [string]$Password)

    if (-not (Test-Path $Path)) { throw "Certificate file not found: $Path" }

    $cert = $null
    try {
        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        if ([string]::IsNullOrEmpty($Password)) {
            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Path, '', $flags)
        }
        else {
            $secure = ConvertTo-SecureString $Password -AsPlainText -Force
            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Path, $secure, $flags)
        }
        return $cert.Subject
    }
    finally {
        if ($null -ne $cert) { $cert.Dispose() }
    }
}

function Get-CommonName {
    param([string]$Subject)
    if ($Subject -match 'CN=([^,]+)') { return $Matches[1].Trim() }
    return $Subject
}

function Update-ManifestIdentity {
    param([string]$Path, [string]$Version, [string]$Publisher)

    [xml]$manifest = Get-Content -Raw -Path $Path
    $identity = $manifest.Package.Identity
    $identity.Version = $Version
    $identity.Publisher = $Publisher

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($true)
    $settings.Indent = $true
    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $manifest.Save($writer)
    }
    finally {
        $writer.Dispose()
    }

    return $manifest.Package.Identity.Name
}

function Write-GitHubOutputs {
    param([hashtable]$Outputs)

    foreach ($key in $Outputs.Keys) {
        $value = [string]$Outputs[$key]
        Write-Host "$key=$value"
        if ($env:GITHUB_OUTPUT) {
            Add-Content -Path $env:GITHUB_OUTPUT -Value "$key=$value"
        }
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "SELFTEST FAILED: $Message" }
}

function Assert-False {
    param([bool]$Condition, [string]$Message)
    if ($Condition) { throw "SELFTEST FAILED: $Message" }
}

function Invoke-SelfTest {
    $valid = @('v1.2.3', 'v0.0.1', 'v65535.65535.65535', 'v1.2.3-preview.1', 'v1.2.3-preview.65534')
    $invalid = @(
        '1.2.3', 'v1', 'v1.2', 'v1.2.3.4', 'v01.2.3', 'v1.02.3', 'v1.2.03',
        'v1.2.3-preview.0', 'v1.2.3-preview.65535', 'v1.2.3-preview.01', 'v1.2.3-preview',
        'v1.2.3-alpha.1', 'v1.2.3+build', 'V1.2.3', 'v1.2.3-preview.-1', 'v',
        'v1.2.3-preview.1.2', 'v1.2.3.4-preview.1'
    )

    foreach ($t in $valid) { Assert-True (Test-StrictTag $t) "expected valid: $t" }
    foreach ($t in $invalid) { Assert-False (Test-StrictTag $t) "expected invalid: $t" }

    # Stable mapping (revision 65535, no leading v in the asset name).
    Assert-True ((Get-MsixVersion 'v1.2.3') -eq '1.2.3.65535') 'stable msix version'
    Assert-True ((Get-SemverFromTag 'v1.2.3') -eq '1.2.3') 'stable semver'
    Assert-True ((Get-MsixAssetName '1.2.3') -eq 'Networker-1.2.3-win-x64.msix') 'stable asset name'
    Assert-True ((Get-ChecksumAssetName 'Networker-1.2.3-win-x64.msix') -eq 'Networker-1.2.3-win-x64.msix.sha256') 'stable checksum name'

    # Preview mapping (revision equals the preview number).
    Assert-True ((Get-MsixVersion 'v1.2.3-preview.4') -eq '1.2.3.4') 'preview msix version'
    Assert-True ((Get-MsixAssetName '1.2.3-preview.4') -eq 'Networker-1.2.3-preview.4-win-x64.msix') 'preview asset name'

    # Optional cross-check against the actual Networker.Core policy, when the
    # build output is available (Release workflow sets NETWORKER_CORE_ASSEMBLY).
    # Failures here are FATAL: the workflow depends on this guard, and a silent
    # skip would let script/core drift ship. Requires pwsh (a .NET 8 host);
    # Windows PowerShell 5.1 cannot load the net8.0 assembly, so running this
    # under 5.1 with the variable set fails the script.
    if ($env:NETWORKER_CORE_ASSEMBLY) {
        $coreDll = [System.IO.Path]::GetFullPath($env:NETWORKER_CORE_ASSEMBLY)
        if (-not (Test-Path $coreDll)) {
            throw "NETWORKER_CORE_ASSEMBLY not found: $coreDll"
        }

        $coreDllDir = Split-Path -Parent $coreDll

        # Resolve dependencies (NuGet.Versioning) from the assembly's own
        # directory first, then from the NuGet cache, so the cross-check is
        # independent of whether the build copied dependencies to output.
        $resolver = {
            param($Sender, $EventArgs)
            $name = $EventArgs.Name.Split(',')[0].Trim()
            $sibling = Join-Path $coreDllDir "$name.dll"
            if (Test-Path $sibling) {
                return [System.Reflection.Assembly]::LoadFrom($sibling)
            }
            $cacheRoot = $env:NETWORKER_NUGET_PACKAGES
            if ([string]::IsNullOrEmpty($cacheRoot)) {
                $cacheRoot = Join-Path $env:USERPROFILE '.nuget\packages'
            }
            $packageDir = Join-Path $cacheRoot $name.ToLowerInvariant()
            if (Test-Path $packageDir) {
                $candidates = Get-ChildItem $packageDir -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending
                foreach ($candidate in $candidates) {
                    try { return [System.Reflection.Assembly]::LoadFrom($candidate.FullName) } catch { }
                }
            }
            return $null
        }
        [System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

        try {
            $assembly = [System.Reflection.Assembly]::LoadFrom($coreDll)
            $policy = $assembly.GetType('Networker.Core.Updates.NetworkerVersionPolicy')
            if ($null -eq $policy) {
                throw 'Networker.Core.Updates.NetworkerVersionPolicy type not found in assembly.'
            }
            $tryParse = $policy.GetMethod('TryParseTag')
            $toMsix = $policy.GetMethod('ToMsixVersion')
            $msixName = $policy.GetMethod('MsixAssetName')
            if ($null -eq $tryParse -or $null -eq $toMsix -or $null -eq $msixName) {
                throw 'Policy methods (TryParseTag/ToMsixVersion/MsixAssetName) not found.'
            }

            # MethodInfo.Invoke returns the method's return value and writes
            # ref/out values back into the passed arguments array.
            foreach ($t in $valid) {
                $invokeArgs = [object[]]@($t, $null)
                $parsed = [bool]$tryParse.Invoke($null, $invokeArgs)
                Assert-True $parsed "core policy rejects valid tag: $t"
                $coreVersion = $invokeArgs[1]
                Assert-True (($toMsix.Invoke($null, [object[]]@($coreVersion)).ToString()) -eq (Get-MsixVersion $t)) "core msix mapping differs for $t"
                Assert-True (($msixName.Invoke($null, [object[]]@($coreVersion)).ToString()) -eq (Get-MsixAssetName (Get-SemverFromTag $t))) "core asset mapping differs for $t"
            }

            foreach ($t in $invalid) {
                $invokeArgs = [object[]]@($t, $null)
                $parsed = [bool]$tryParse.Invoke($null, $invokeArgs)
                Assert-False $parsed "core policy accepts invalid tag: $t"
            }

            # The actual release tag about to ship must agree with the Core
            # policy too, not just the fixed test vectors, so a drift that only
            # affects this specific tag cannot slip through.
            if ($Tag) {
                Assert-True (Test-StrictTag $Tag) "invalid tag passed with -SelfTest: $Tag"
                $invokeArgs = [object[]]@($Tag, $null)
                $parsed = [bool]$tryParse.Invoke($null, $invokeArgs)
                Assert-True $parsed "core policy rejects release tag: $Tag"
                $coreVersion = $invokeArgs[1]
                Assert-True (($toMsix.Invoke($null, [object[]]@($coreVersion)).ToString()) -eq (Get-MsixVersion $Tag)) "core msix version mapping differs for release tag $Tag"
                Assert-True (($msixName.Invoke($null, [object[]]@($coreVersion)).ToString()) -eq (Get-MsixAssetName (Get-SemverFromTag $Tag))) "core asset name mapping differs for release tag $Tag"
                Write-Host "Cross-checked release tag mapping: $Tag"
            }

            Write-Host "Cross-checked against Networker.Core assembly: $coreDll"
        }
        finally {
            [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
        }
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    Write-Host 'Prepare-Release self-test passed.'
    exit 0
}

if ([string]::IsNullOrEmpty($Tag)) {
    throw 'Tag is required (e.g. v1.2.3 or v1.2.3-preview.1).'
}
if (-not (Test-StrictTag $Tag)) {
    throw "Invalid release tag: '$Tag'. Expected vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-preview.N (N in 1..65534)."
}
Write-Host "Validated release tag: $Tag"

$Semver = Get-SemverFromTag $Tag
$MsixVersion = Get-MsixVersion $Tag
$MsixAssetName = Get-MsixAssetName $Semver
$ChecksumAssetName = Get-ChecksumAssetName $MsixAssetName
$Prerelease = $Tag.Contains('-')

$Publisher = ''
$PublisherDisplayName = ''
$PackageName = ''

if ($CertificatePath) {
    $Publisher = Get-CertificateSubject -Path $CertificatePath -Password $CertificatePassword
    $PublisherDisplayName = Get-CommonName $Publisher
    Write-Host 'Loaded signing certificate subject (certificate data is not printed).'
}

if ($ManifestPath) {
    if (-not $Publisher) {
        throw 'ManifestPath requires -CertificatePath so the Publisher subject can be set.'
    }
    $PackageName = Update-ManifestIdentity -Path $ManifestPath -Version $MsixVersion -Publisher $Publisher
    Write-Host "Patched $ManifestPath (Identity Version=$MsixVersion Publisher=$Publisher)"
}

$outputs = [ordered]@{
    tag                   = $Tag
    semver                = $Semver
    msix_version          = $MsixVersion
    asset_name            = $MsixAssetName
    checksum_name         = $ChecksumAssetName
    appinstaller_name     = 'Networker-x64.appinstaller'
    prerelease            = $Prerelease.ToString().ToLowerInvariant()
    package_name          = $PackageName
    publisher             = $Publisher
    publisher_display_name = $PublisherDisplayName
}

Write-GitHubOutputs -Outputs $outputs
