param([string]$BaseUri = 'http://localhost:8080')

$ErrorActionPreference = 'Stop'
$containers = podman inspect modular-monolith_sqlserver_1 modular-monolith_seed_1 modular-monolith_webapi_1 | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the deployed containers.' }
$sql, $seed, $api = $containers
if ($sql.State.Health.Status -ne 'healthy') { throw 'SQL Server is not healthy.' }
if ($seed.State.Status -ne 'exited' -or $seed.State.ExitCode -ne 0) { throw 'Seed did not finish successfully.' }
if ($api.State.Status -ne 'running') { throw 'Web API is not running.' }

$response = Invoke-WebRequest "$BaseUri/swagger/index.html" -TimeoutSec 30
if ($response.StatusCode -ne 200) { throw 'Swagger is unavailable.' }
$products = Invoke-RestMethod "$BaseUri/products/" -TimeoutSec 30
if ($products.Count -lt 30) { throw 'Expected at least 30 seeded products.' }
Invoke-RestMethod "$BaseUri/orders/" -TimeoutSec 30 | Out-Null

# Use the deployed secret without writing it or the signed tokens to output.
$key = ($api.Config.Env | Where-Object { $_.StartsWith('Jwt__Key=') }).Substring(9)
if ($key.Length -lt 32) { throw 'JWT signing key must contain at least 32 characters.' }

function ConvertTo-Base64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($key))
try {
    foreach ($tenant in @('tenant1', 'tenant2')) {
        $claims = @{
            iss = 'ModularMonolith'; aud = 'ModularMonolith'
            sub = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'; tenant_id = $tenant
            exp = [DateTimeOffset]::UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        } | ConvertTo-Json -Compress
        $payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($claims))
        $signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$header.$payload")))
        $headers = @{ Authorization = "Bearer $header.$payload.$signature" }
        Invoke-RestMethod "$BaseUri/users/" -Headers $headers -TimeoutSec 60 | Out-Null
        Invoke-RestMethod "$BaseUri/datasources/" -Headers $headers -TimeoutSec 30 | Out-Null
    }
}
finally { $hmac.Dispose() }

Write-Host "PASS: SQL healthy; seed exited 0; Swagger, $($products.Count) products, orders and both tenant APIs responded successfully."
