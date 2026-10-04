[CmdletBinding()]
param([ValidateSet('docker', 'podman')][string]$ContainerRuntime = 'docker')

$ErrorActionPreference = 'Stop'
$seedJson = & $ContainerRuntime inspect modular-monolith_seed_1
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the local seed container.' }
$seed = ($seedJson | ConvertFrom-Json)[0]
$emailEntry = $seed.Config.Env | Where-Object { $_.StartsWith('Seed__AdministratorEmail=') } | Select-Object -First 1
$passwordEntry = $seed.Config.Env | Where-Object { $_.StartsWith('Seed__AdministratorPassword=') } | Select-Object -First 1
if (-not $emailEntry -or -not $passwordEntry) { throw 'The local seed administrator credentials are unavailable.' }
$email = $emailEntry.Substring('Seed__AdministratorEmail='.Length)
$password = $passwordEntry.Substring('Seed__AdministratorPassword='.Length) | ConvertTo-SecureString -AsPlainText -Force
[PSCredential]::new($email, $password)
