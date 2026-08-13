[CmdletBinding()]
param(
    [string]$Repository = 'NormalDudeBro/networker',
    [string]$Environment = 'production-release',
    [string]$KeyId = ('networker-feed-' + (Get-Date -Format 'yyyy-MM')),
    [string]$SigningCertificatePath,
    [string]$SigningCertificatePassword,
    [switch]$ReplaceAuthenticodeSecrets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) is required.' }
gh auth status | Out-Null
if ($LASTEXITCODE) { throw 'GitHub CLI is not authenticated.' }

if ($ReplaceAuthenticodeSecrets) {
    if (-not $SigningCertificatePath -or -not (Test-Path $SigningCertificatePath)) {
        throw 'A publicly trusted code-signing PFX is required with -ReplaceAuthenticodeSecrets.'
    }
    if ([string]::IsNullOrEmpty($SigningCertificatePassword)) { throw 'The PFX password is required.' }
    $flags = [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        [IO.Path]::GetFullPath($SigningCertificatePath),
        $SigningCertificatePassword,
        $flags)
    try {
        if (-not $certificate.HasPrivateKey) { throw 'The PFX does not contain a private key.' }
        $eku = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' })
        if ($eku.Count -eq 0 -or $eku[0].Format($false) -notmatch '1\.3\.6\.1\.5\.5\.7\.3\.3|Code Signing') {
            throw 'The certificate is not valid for code signing.'
        }
        $pfxBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($SigningCertificatePath))
        $pfxBase64 | gh secret set NETWORKER_SIGNING_CERTIFICATE_BASE64 --env $Environment --repo $Repository
        if ($LASTEXITCODE) { throw 'Failed to set the Authenticode PFX secret.' }
        $SigningCertificatePassword | gh secret set NETWORKER_SIGNING_CERTIFICATE_PASSWORD --env $Environment --repo $Repository
        if ($LASTEXITCODE) { throw 'Failed to set the Authenticode password secret.' }
    }
    finally { $certificate.Dispose() }
}

$curve = [Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256')
$key = [Security.Cryptography.ECDsa]::Create($curve)
try {
    $privatePemBase64 = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes($key.ExportPkcs8PrivateKeyPem()))
    $publicSpkiBase64 = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())

    $privatePemBase64 | gh secret set NETWORKER_UPDATE_FEED_PRIVATE_KEY_PEM_BASE64 --env $Environment --repo $Repository
    if ($LASTEXITCODE) { throw 'Failed to set the feed private-key secret.' }
    $publicSpkiBase64 | gh secret set NETWORKER_UPDATE_FEED_PUBLIC_KEY_SPKI_BASE64 --env $Environment --repo $Repository
    if ($LASTEXITCODE) { throw 'Failed to set the feed public-key secret.' }
    $KeyId | gh secret set NETWORKER_UPDATE_FEED_KEY_ID --env $Environment --repo $Repository
    if ($LASTEXITCODE) { throw 'Failed to set the feed key-ID secret.' }
}
finally { $key.Dispose() }

Write-Host "Configured Networker release secrets for $Repository / $Environment."
if (-not $ReplaceAuthenticodeSecrets) {
    Write-Warning 'Authenticode secrets were preserved. They are optional and needed only to remove Windows publisher/reputation warnings in a future trusted-signing release mode.'
}
