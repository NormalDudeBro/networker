[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$FeedPath,
    [Parameter(Mandatory = $true)][string]$KeyId,
    [Parameter(Mandatory = $true)][string]$PrivateKeyPath,
    [string]$OutputPath = "$FeedPath.sig"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$feed = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($FeedPath))
$pem = [IO.File]::ReadAllText([IO.Path]::GetFullPath($PrivateKeyPath))
$ecdsa = [Security.Cryptography.ECDsa]::Create()
try {
    $ecdsa.ImportFromPem($pem)
    if ($ecdsa.KeySize -ne 256) { throw 'The update signing key must be ECDSA P-256.' }
    $signature = $ecdsa.SignData($feed, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $envelope = [ordered]@{ Schema = 1; KeyId = $KeyId; Signature = [Convert]::ToBase64String($signature) }
    $json = $envelope | ConvertTo-Json -Compress
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false))
}
finally { $ecdsa.Dispose() }
Write-Host "Signed exact feed bytes: $OutputPath"
