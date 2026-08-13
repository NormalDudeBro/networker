[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Tag)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^v(0|[1-9][0-9]{0,9})\.(0|[1-9][0-9]{0,9})\.(0|[1-9][0-9]{0,9})(-preview\.([1-9][0-9]{0,9}))?$') {
    throw "Invalid Networker release tag: $Tag"
}
$numbers = @($Matches[1], $Matches[2], $Matches[3])
if ($Matches[5]) { $numbers += $Matches[5] }
foreach ($number in $numbers) {
    $parsed = 0
    if (-not [int]::TryParse($number, [ref]$parsed)) { throw "Release tag component exceeds Int32: $number" }
}
$version = $Tag.Substring(1)
$preview = $version.Contains('-preview.')
$channel = if ($preview) { 'preview-win-x64' } else { 'win-x64' }
$outputs = [ordered]@{
    version = $version
    channel = $channel
    prerelease = $preview.ToString().ToLowerInvariant()
    package_name = "Networker-$version-win-x64.zip"
    feed_name = "releases.$channel.json"
    signature_name = "releases.$channel.json.sig"
}
foreach ($entry in $outputs.GetEnumerator()) {
    Write-Host "$($entry.Key)=$($entry.Value)"
    if ($env:GITHUB_OUTPUT) { Add-Content $env:GITHUB_OUTPUT "$($entry.Key)=$($entry.Value)" }
}
