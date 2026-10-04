# Requires PowerShell 7 and the repository's .NET SDK. Run once before local Aspire/Compose.
[CmdletBinding()]
param([switch]$SkipTrust)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$appHost = Join-Path $repository 'src/ModularMonolith.AppHost/ModularMonolith.AppHost.csproj'
$certificateRoot = Join-Path $repository 'artifacts/certificates'
$httpsDirectory = Join-Path $certificateRoot 'https'
$protectionDirectory = Join-Path $certificateRoot 'dataprotection'
$settingsDirectory = Join-Path $repository 'artifacts/config'
foreach ($directory in @($httpsDirectory, $protectionDirectory, $settingsDirectory, (Join-Path $repository 'artifacts/dpkeys'))) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

if (-not $SkipTrust) {
    & dotnet dev-certs https --trust
    if ($LASTEXITCODE -ne 0) { throw 'The local HTTPS certificate could not be trusted.' }
}
& dotnet dev-certs https --export-path (Join-Path $httpsDirectory 'tls.pem') --format Pem --no-password
if ($LASTEXITCODE -ne 0) { throw 'The local HTTPS certificate could not be exported.' }

$protectionCertificate = Join-Path $protectionDirectory 'dataprotection.pfx'
$secretLines = & dotnet user-secrets list --project $appHost
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the AppHost local secret store.' }
$passwordLine = $secretLines | Where-Object { $_.StartsWith('Parameters:dp-certificate-password = ') } | Select-Object -First 1
if (Test-Path -LiteralPath $protectionCertificate) {
    if (-not $passwordLine) { throw 'The existing Data Protection certificate must be kept. Restore its Parameters:dp-certificate-password user-secret from backup.' }
}
else {
    $certificatePassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    $certificate = $null
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=ModularMonolith Local Data Protection', $rsa,
            [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddYears(3))
        [IO.File]::WriteAllBytes($protectionCertificate, $certificate.Export(
            [Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $certificatePassword))
        @{ 'Parameters:dp-certificate-password' = $certificatePassword } | ConvertTo-Json -Compress |
            & dotnet user-secrets set --project $appHost | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot save the certificate password to the AppHost user-secret store.' }
    }
    finally {
        if ($certificate) { $certificate.Dispose() }
        $rsa.Dispose()
        $certificatePassword = $null
    }
}

$settingsFile = Join-Path $settingsDirectory 'authsettings.json'
if (-not (Test-Path -LiteralPath $settingsFile)) {
    Copy-Item -LiteralPath (Join-Path $repository 'deploy/authsettings.example.json') -Destination $settingsFile
}

Write-Host 'Local certificates and settings are ready under ignored artifacts/. Existing DP certificates and settings were preserved.'
Write-Host 'The DP certificate password is stored in the AppHost user-secrets, never in source. Protect and back up both.'
