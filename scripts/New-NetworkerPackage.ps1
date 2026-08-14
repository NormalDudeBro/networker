[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [ValidateSet('win-x64', 'preview-win-x64')][string]$Channel = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts\release',
    [string]$IsccPath,
    [string]$UpdateFeedKeyId,
    [string]$UpdateFeedPublicKeyBase64,
    [string]$SignToolPath,
    [string]$SigningCertificatePath,
    [string]$SigningCertificatePassword,
    [switch]$RequireFeedTrust,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$appPublish = Join-Path $root 'artifacts\publish\app'
$launcherPublish = Join-Path $root 'artifacts\publish\launcher'
$hostPublish = Join-Path $root 'artifacts\publish\update-host'
$bootstrapPublish = Join-Path $root 'artifacts\publish\bootstrap'
$installerStage = Join-Path $root 'artifacts\installer-staging'
$slotStage = Join-Path $installerStage 'app-a'

if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-preview\.[1-9][0-9]*)?$') { throw "Invalid semantic version: $Version" }
if (($Channel -eq 'win-x64') -eq $Version.Contains('-preview.')) { throw "Version '$Version' does not match '$Channel'." }
if ($RequireFeedTrust -and ([string]::IsNullOrWhiteSpace($UpdateFeedKeyId) -or [string]::IsNullOrWhiteSpace($UpdateFeedPublicKeyBase64))) {
    throw 'Release packaging requires a pinned update-feed public key and key ID.'
}
if ($SigningCertificatePassword -and $SigningCertificatePassword.Contains('"')) { throw 'The signing certificate password cannot contain a double quote.' }
$fileVersion = "$($Version.Split('-')[0]).0"

if (-not $NoBuild) {
    Remove-Item $appPublish, $launcherPublish, $hostPublish, $bootstrapPublish -Recurse -Force -ErrorAction SilentlyContinue
    $properties = @(
        "-p:NetworkerVersion=$Version"
        "-p:Version=$Version"
        "-p:InformationalVersion=$Version"
        "-p:FileVersion=$fileVersion"
    )
    if ($UpdateFeedKeyId) { $properties += "-p:NetworkerUpdateKeyId=$UpdateFeedKeyId" }
    if ($UpdateFeedPublicKeyBase64) { $properties += "-p:NetworkerUpdatePublicKeyBase64=$UpdateFeedPublicKeyBase64" }
    dotnet publish (Join-Path $root 'networker.csproj') -c $Configuration -p:Platform=x64 -p:WindowsPackageType=None @properties -o $appPublish
    if ($LASTEXITCODE) { throw 'App publish failed.' }
    dotnet publish (Join-Path $root 'Networker.Launcher\Networker.Launcher.csproj') -c $Configuration -p:Platform=x64 @properties -o $launcherPublish
    if ($LASTEXITCODE) { throw 'Launcher publish failed.' }
    dotnet publish (Join-Path $root 'Networker.UpdateHost\Networker.UpdateHost.csproj') -c $Configuration @properties -o $hostPublish
    if ($LASTEXITCODE) { throw 'Update host publish failed.' }
    dotnet publish (Join-Path $root 'Networker.Bootstrap\Networker.Bootstrap.csproj') -c $Configuration @properties -o $bootstrapPublish
    if ($LASTEXITCODE) { throw 'Bootstrap publish failed.' }
}

$codexSource = Join-Path $root 'artifacts\codex\win-x64'
if (-not (Test-Path (Join-Path $codexSource 'bin\codex-app-server.exe'))) {
    & (Join-Path $root 'scripts\Get-CodexAppServer.ps1') -OutputDirectory 'artifacts\codex\win-x64'
    if ($LASTEXITCODE) { throw 'Codex app-server acquisition failed.' }
}
if (-not (Test-Path (Join-Path $codexSource 'codex-package.json'))) {
    throw "Codex package is missing at $codexSource. Run scripts/Get-CodexAppServer.ps1."
}

Remove-Item $installerStage -Recurse -Force -ErrorAction SilentlyContinue
New-Item $slotStage -ItemType Directory -Force | Out-Null
New-Item (Join-Path $installerStage 'root') -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $appPublish '*') $slotStage -Recurse -Force
$codexDest = Join-Path $slotStage 'Codex'
Remove-Item $codexDest -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $codexSource $codexDest -Recurse -Force
$notices = Join-Path $root 'THIRD-PARTY-NOTICES.txt'
if (Test-Path $notices) {
    Copy-Item $notices (Join-Path $slotStage 'THIRD-PARTY-NOTICES.txt') -Force
    Copy-Item $notices (Join-Path $codexDest 'THIRD-PARTY-NOTICES.txt') -Force
}
Get-ChildItem $slotStage -Filter '*.pdb' -Recurse | Remove-Item -Force
foreach ($pair in @(
    @((Join-Path $launcherPublish 'Networker.Launcher.exe'), (Join-Path $slotStage 'Networker.Launcher.exe')),
    @((Join-Path $hostPublish 'Networker.UpdateHost.exe'), (Join-Path $slotStage 'Networker.UpdateHost.exe')),
    @((Join-Path $bootstrapPublish 'Networker.exe'), (Join-Path $installerStage 'root\Networker.exe'))
)) {
    if (-not (Test-Path $pair[0])) { throw "Required publish output missing: $($pair[0])" }
    Copy-Item $pair[0] $pair[1] -Force
}
[IO.File]::WriteAllText((Join-Path $slotStage 'version.txt'), "$Version`n", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $installerStage 'root\active-slot.txt'), "app-a`n", [Text.UTF8Encoding]::new($false))

if ($SigningCertificatePath) {
    if (-not $SignToolPath) {
        $SignToolPath = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object FullName -Match '\\x64\\signtool\.exe$' | Sort-Object FullName -Descending | Select-Object -ExpandProperty FullName -First 1
    }
    if (-not $SignToolPath -or -not (Test-Path $SignToolPath)) { throw 'signtool.exe was not found.' }
    $ownedBinaries = @(
        (Join-Path $installerStage 'root\Networker.exe')
        (Join-Path $slotStage 'networker.exe')
        (Join-Path $slotStage 'networker.dll')
        (Join-Path $slotStage 'Networker.Core.dll')
        (Join-Path $slotStage 'Networker.Update.Contracts.dll')
        (Join-Path $slotStage 'Networker.Launcher.exe')
        (Join-Path $slotStage 'Networker.UpdateHost.exe')
    )
    foreach ($binary in $ownedBinaries) {
        if (-not (Test-Path $binary)) { throw "Signing input is missing: $binary" }
        & $SignToolPath sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f $SigningCertificatePath /p $SigningCertificatePassword $binary
        if ($LASTEXITCODE) { throw "Authenticode signing failed: $binary" }
        & $SignToolPath verify /pa $binary
        if ($LASTEXITCODE) { throw "Authenticode verification failed: $binary" }
    }
}

New-Item $output -ItemType Directory -Force | Out-Null
$zip = Join-Path $output "Networker-$Version-win-x64.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($slotStage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{ Schema = 1; PackageId = 'Networker.Desktop'; Version = $Version; Channel = $Channel; FileName = [IO.Path]::GetFileName($zip); Size = (Get-Item $zip).Length; Sha256 = $hash }
$feed = Join-Path $output "releases.$Channel.json"
[IO.File]::WriteAllText($feed, ($manifest | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))

if (-not $IsccPath) {
    $candidates = @(
        (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
        "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }
    $IsccPath = $candidates | Select-Object -First 1
}
if (-not $IsccPath) { throw 'Inno Setup ISCC.exe was not found. Install Inno Setup 7 or pass -IsccPath.' }

$isccArguments = @(
    (Join-Path $root 'installer\Networker.iss')
    "/DAppVersion=$Version"
    "/DSourceDir=$installerStage"
    "/DOutputDir=$output"
)
if ($SigningCertificatePath) {
    $signCommand = '"' + $SignToolPath + '" sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f "' +
        $SigningCertificatePath + '" /p "' + $SigningCertificatePassword + '" $f'
    $isccArguments += '/DSignToolName=NetworkerSign'
    $isccArguments += "/SNetworkerSign=$signCommand"
}
& $IsccPath @isccArguments
if ($LASTEXITCODE) { throw 'Inno Setup compilation failed.' }
if ($SigningCertificatePath) {
    $setup = Join-Path $output 'Networker-Setup.exe'
    & $SignToolPath verify /pa $setup
    if ($LASTEXITCODE) { throw 'Setup Authenticode verification failed.' }
}
Write-Host "Created Networker $Version installer and update package in $output"
