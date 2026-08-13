[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Directory,
    [Parameter(Mandatory = $true)][string]$Version,
    [ValidateSet('win-x64', 'preview-win-x64')][string]$Channel = 'win-x64',
    [string]$PublicKeyPath,
    [string]$KeyId,
    [switch]$RequireAuthenticode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$directory = [IO.Path]::GetFullPath($Directory)
$setup = Join-Path $directory 'Networker-Setup.exe'
$feed = Join-Path $directory "releases.$Channel.json"
$signature = "$feed.sig"
if (-not (Test-Path $setup) -or -not (Test-Path $feed)) { throw 'Setup or release manifest is missing.' }

$manifest = Get-Content -Raw $feed | ConvertFrom-Json
if ($manifest.Schema -ne 1 -or $manifest.PackageId -ne 'Networker.Desktop' -or $manifest.Version -ne $Version -or $manifest.Channel -ne $Channel) {
    throw 'Release manifest identity is invalid.'
}
$package = Join-Path $directory ([string]$manifest.FileName)
if (-not (Test-Path $package) -or (Get-Item $package).Length -ne [long]$manifest.Size) { throw 'Release ZIP size is invalid.' }
$hash = (Get-FileHash $package -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -cne ([string]$manifest.Sha256).ToLowerInvariant()) { throw 'Release ZIP SHA-256 is invalid.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
$authenticodeDirectory = $null
try {
    $names = @($archive.Entries | ForEach-Object FullName)
    foreach ($forbidden in @('ChatGptWebView', 'agent-journal', 'troubleshooting-workspace.json', 'settings.json', '.env')) {
        if ($names | Where-Object { $_ -like "*$forbidden*" }) { throw "Release ZIP contains local runtime state: $forbidden" }
    }
    foreach ($required in @('networker.exe', 'Networker.Launcher.exe', 'Networker.UpdateHost.exe', 'version.txt')) {
        if ($names -notcontains $required) { throw "Release ZIP is missing $required." }
    }
    if ($RequireAuthenticode) {
        $authenticodeDirectory = Join-Path $env:TEMP ('networker-authenticode-' + [guid]::NewGuid().ToString('N'))
        New-Item $authenticodeDirectory -ItemType Directory | Out-Null
        foreach ($name in @('networker.exe', 'networker.dll', 'Networker.Core.dll', 'Networker.Update.Contracts.dll', 'Networker.Launcher.exe', 'Networker.UpdateHost.exe')) {
            $entry = $archive.GetEntry($name)
            if (-not $entry) { throw "Release ZIP is missing signed Networker binary $name." }
            $path = Join-Path $authenticodeDirectory $name
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path)
            $signatureStatus = Get-AuthenticodeSignature $path
            if ($signatureStatus.Status -ne 'Valid' -or -not $signatureStatus.TimeStamperCertificate) {
                throw "Networker binary is not validly signed and timestamped: $name ($($signatureStatus.Status))."
            }
        }
    }
}
finally {
    $archive.Dispose()
    if ($authenticodeDirectory) { Remove-Item $authenticodeDirectory -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($PublicKeyPath) {
    if (-not $KeyId -or -not (Test-Path $signature)) { throw 'Feed signature/key id is missing.' }
    $envelope = Get-Content -Raw $signature | ConvertFrom-Json
    if ($envelope.Schema -ne 1 -or $envelope.KeyId -ne $KeyId) { throw 'Feed signature envelope is invalid.' }
    $ecdsa = [Security.Cryptography.ECDsa]::Create()
    try {
        $ecdsa.ImportFromPem([IO.File]::ReadAllText([IO.Path]::GetFullPath($PublicKeyPath)))
        if (-not $ecdsa.VerifyData([IO.File]::ReadAllBytes($feed), [Convert]::FromBase64String($envelope.Signature), [Security.Cryptography.HashAlgorithmName]::SHA256)) {
            throw 'Feed signature verification failed.'
        }
    }
    finally { $ecdsa.Dispose() }
}

if ($RequireAuthenticode) {
    $setupSignature = Get-AuthenticodeSignature $setup
    if ($setupSignature.Status -ne 'Valid' -or -not $setupSignature.TimeStamperCertificate) {
        throw 'Setup Authenticode signature or RFC 3161 timestamp verification failed.'
    }
}
Write-Host "Verified Networker $Version artifacts in $directory"
