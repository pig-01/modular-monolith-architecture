# Logs in through the public HTTPS browser boundary. It never mints JWTs or prints credentials.
[CmdletBinding()]
param(
    [uri]$BaseUri = 'https://localhost:8443',
    [ValidateSet('docker', 'podman')][string]$ContainerRuntime = 'docker',
    [PSCredential]$Credential,
    [switch]$SkipCertificateCheck
)

$ErrorActionPreference = 'Stop'
if ($BaseUri.Scheme -ne 'https') { throw 'Deployment checks require HTTPS for Secure cookies.' }
$containers = & $ContainerRuntime inspect modular-monolith_sqlserver_1 modular-monolith_seed_1 modular-monolith_webapi_1 modular-monolith_frontend_1 | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the deployed containers.' }
$sql, $seed, $api, $frontend = $containers
if ($sql.State.Health.Status -ne 'healthy') { throw 'SQL Server is not healthy.' }
if ($seed.State.Status -ne 'exited' -or $seed.State.ExitCode -ne 0) { throw 'Seed did not finish successfully.' }
if ($api.State.Status -ne 'running' -or $frontend.State.Status -ne 'running') { throw 'API and frontend must both be running.' }
if ($api.HostConfig.PortBindings.PSObject.Properties.Count -gt 0) { throw 'The API should not expose a host port in Compose.' }
if (-not $Credential) {
    $Credential = & (Join-Path $PSScriptRoot 'Get-LocalAdministratorCredential.ps1') -ContainerRuntime $ContainerRuntime
}

$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$common = @{ WebSession = $session; TimeoutSec = 30; SkipHttpErrorCheck = $true }
if ($SkipCertificateCheck) { $common.SkipCertificateCheck = $true }
$base = $BaseUri.AbsoluteUri.TrimEnd('/')
function Assert-Status($Response, [int[]]$Expected, [string]$Operation) {
    if ($Response.StatusCode -notin $Expected) { throw "$Operation returned HTTP $($Response.StatusCode); expected $($Expected -join '/')." }
}
function Get-CsrfToken {
    $response = Invoke-WebRequest "$base/api/auth/csrf" @common
    Assert-Status $response @(200) 'CSRF bootstrap'
    $token = ($response.Content | ConvertFrom-Json).requestToken
    if ([string]::IsNullOrEmpty($token)) { throw 'CSRF bootstrap did not return requestToken.' }
    return $token
}

$page = Invoke-WebRequest "$base/login" @common
Assert-Status $page @(200) 'SPA deep link'
foreach ($header in @('Content-Security-Policy', 'Strict-Transport-Security', 'X-Content-Type-Options', 'Referrer-Policy')) {
    if (-not $page.Headers.ContainsKey($header)) { throw "Frontend response is missing $header." }
}
if (($page.Headers['Content-Security-Policy'] -join ' ') -match "unsafe-inline|unsafe-eval") { throw 'Production CSP allows inline scripts or evaluation.' }
$anonymous = Invoke-WebRequest "$base/api/products/" @common
Assert-Status $anonymous @(401) 'Anonymous business API'
$csrf = Get-CsrfToken
$body = @{ email = $Credential.UserName; password = $Credential.GetNetworkCredential().Password } | ConvertTo-Json -Compress
try {
    $login = Invoke-WebRequest "$base/api/auth/login" -Method Post -ContentType 'application/json' -Body $body -Headers @{ 'X-CSRF-TOKEN' = $csrf } @common
    Assert-Status $login @(200, 204) 'Administrator login'
}
finally { $body = $null }
$authCookies = @($session.Cookies.GetCookies($BaseUri) | Where-Object { $_.Name.StartsWith('__Host-') })
if ($authCookies.Count -lt 2 -or @($authCookies | Where-Object { -not $_.Secure -or -not $_.HttpOnly }).Count -gt 0) {
    throw 'Expected Secure, HttpOnly __Host- authentication cookies.'
}
$me = Invoke-WebRequest "$base/api/auth/me" @common
Assert-Status $me @(200) 'Authenticated profile'
$productsResponse = Invoke-WebRequest "$base/api/products/" @common
Assert-Status $productsResponse @(200) 'Administrator products API'
$products = $productsResponse.Content | ConvertFrom-Json
if ($products.Count -lt 30) { throw 'Expected at least 30 seeded products.' }
foreach ($endpoint in @('orders/', 'users/', 'datasources/')) {
    $response = Invoke-WebRequest "$base/api/$endpoint" @common
    Assert-Status $response @(200) "Administrator $endpoint API"
}
$profile = $me.Content | ConvertFrom-Json
foreach ($tenant in $profile.tenants) {
    $csrf = Get-CsrfToken
    $switchBody = @{ tenantId = $tenant.id; requestId = [guid]::NewGuid().ToString() } | ConvertTo-Json -Compress
    $switched = Invoke-WebRequest "$base/api/auth/switch-tenant" -Method Post -ContentType 'application/json' -Body $switchBody -Headers @{ 'X-CSRF-TOKEN' = $csrf } @common
    Assert-Status $switched @(200) 'Tenant switch'
    $current = Invoke-WebRequest "$base/api/auth/me" @common
    Assert-Status $current @(200) 'Profile after tenant switch'
    if (($current.Content | ConvertFrom-Json).currentTenant.id -ne $tenant.id) { throw 'Tenant switch did not persist in the session.' }
    $tenantUsers = Invoke-WebRequest "$base/api/users/" @common
    Assert-Status $tenantUsers @(200) 'Migrated tenant users API'
}
# Invoke-WebRequest retains explicitly supplied headers in WebSession across requests.
$null = $session.Headers.Remove('X-CSRF-TOKEN')
$invalidCsrf = Invoke-WebRequest "$base/api/auth/logout" -Method Post -ContentType 'application/json' -Body '{}' @common
Assert-Status $invalidCsrf @(400, 403) 'Missing CSRF rejection'
$csrf = Get-CsrfToken
$logout = Invoke-WebRequest "$base/api/auth/logout" -Method Post -ContentType 'application/json' -Body '{}' -Headers @{ 'X-CSRF-TOKEN' = $csrf } @common
Assert-Status $logout @(200, 204) 'Logout'
$afterLogout = Invoke-WebRequest "$base/api/auth/me" @common
Assert-Status $afterLogout @(401) 'Revoked session'
Write-Host "PASS: SQL healthy; seed exited 0; HTTPS SPA/security headers; real cookie login; $($products.Count) products and protected APIs; CSRF rejection; logout."
