# Prepare the local Docker Compose inputs without starting/stopping any service.
[CmdletBinding()]
param([switch]$SkipTrust)

$ErrorActionPreference = 'Stop'
& docker info --format '{{.ServerVersion}}' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Docker must be running before preparing deployment secrets or volumes.' }
$repository = Split-Path -Parent $PSScriptRoot
$certificate = Join-Path $repository 'artifacts/certificates/dataprotection/dataprotection.pfx'
# A later normal run must still trust TLS after an earlier -SkipTrust setup.
if (-not $SkipTrust -or -not (Test-Path -LiteralPath $certificate) -or
    -not (Test-Path -LiteralPath (Join-Path $repository 'artifacts/certificates/https/tls.pem')) -or
    -not (Test-Path -LiteralPath (Join-Path $repository 'artifacts/certificates/https/tls.key')) -or
    -not (Test-Path -LiteralPath (Join-Path $repository 'artifacts/config/authsettings.json'))) {
    & (Join-Path $PSScriptRoot 'Setup-LocalCertificates.ps1') -SkipTrust:$SkipTrust
}
$environmentPath = Join-Path $repository 'artifacts/compose/.env.local'
New-Item -ItemType Directory -Path (Split-Path -Parent $environmentPath) -Force | Out-Null
$values = [ordered]@{}
if (Test-Path -LiteralPath $environmentPath) {
    foreach ($line in [IO.File]::ReadAllLines($environmentPath)) {
        if ($line -match '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
            $value = $Matches[2].Trim()
            if (($value.StartsWith("'") -and $value.EndsWith("'")) -or ($value.StartsWith('"') -and $value.EndsWith('"'))) {
                $value = $value.Substring(1, $value.Length - 2)
            }
            $values[$Matches[1]] = $value
        }
    }
}

function New-LocalSecret {
    [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) + 'aA1!'
}
# Never pair an existing SQL data volume with a newly generated, unrelated SA password.
if ((-not $values.Contains('SQLSERVER_PASSWORD') -or [string]::IsNullOrWhiteSpace($values['SQLSERVER_PASSWORD'])) -and
    -not [Environment]::GetEnvironmentVariable('SQLSERVER_PASSWORD')) {
    & docker volume inspect modular-monolith_sqlserver-data *> $null
    if ($LASTEXITCODE -eq 0) {
        throw 'An existing SQL volume has no saved password in artifacts/compose/.env.local. Supply its original SQLSERVER_PASSWORD environment variable; the volume and password were not changed.'
    }
}
foreach ($name in @('SQLSERVER_PASSWORD', 'JWT_KEY', 'ADMIN_PASSWORD')) {
    if (-not $values.Contains($name) -or [string]::IsNullOrWhiteSpace($values[$name])) {
        # Explicitly provided previous deployment values win over newly generated defaults.
        $provided = [Environment]::GetEnvironmentVariable($name)
        $values[$name] = if ($provided) { $provided } else { New-LocalSecret }
    }
}
$appHost = Join-Path $repository 'src/ModularMonolith.AppHost/ModularMonolith.AppHost.csproj'
$secretLines = & dotnet user-secrets list --project $appHost
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the local DP certificate password.' }
$entry = $secretLines | Where-Object { $_.StartsWith('Parameters:dp-certificate-password = ') } | Select-Object -First 1
if (-not $entry) { throw 'Restore Parameters:dp-certificate-password in AppHost user-secrets before proceeding.' }
$password = $entry.Substring('Parameters:dp-certificate-password = '.Length)
$loadedCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate, $password)
$loadedCertificate.Dispose()
if ($values.Contains('DP_CERTIFICATE_PASSWORD') -and $values['DP_CERTIFICATE_PASSWORD'] -ne $password) {
    throw 'The existing Compose DP password differs from local certificate setup. Restore the matching certificate/password; existing secrets were not overwritten.'
}
$values['DP_CERTIFICATE_PASSWORD'] = $password
if (-not $values.Contains('ADMIN_EMAIL')) { $values['ADMIN_EMAIL'] = 'admin@example.test' }
if (-not $values.Contains('JWT_KEY_ID')) { $values['JWT_KEY_ID'] = 'local-1' }
foreach ($pair in $values.GetEnumerator()) {
    if ([string]$pair.Value -match "[\r\n']") { throw "Environment value $($pair.Key) requires manual dotenv quoting; existing secrets were not overwritten." }
}
$lines = @('# Local deployment secrets. Never commit or attach this file to an issue.')
$lines += $values.GetEnumerator() | ForEach-Object { "{0}='{1}'" -f $_.Key, $_.Value }
[IO.File]::WriteAllLines($environmentPath, $lines, [Text.UTF8Encoding]::new($false))

# Idempotent volume creation preserves existing SQL data and encryption keys.
& docker volume create modular-monolith_sqlserver-data | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare the SQL external volume; ensure Docker is running.' }
& docker volume create modular-monolith_dataprotection-keys | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare the Data Protection external volume.' }
Write-Host 'Local Compose inputs are ready; existing secrets and external volumes were preserved.'
Write-Host 'Start with: docker compose --env-file artifacts/compose/.env.local up --build -d'
