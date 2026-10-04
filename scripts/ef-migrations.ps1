Param(
    [string]$Startup = "apps\\webapi\\ModularMonolith.WebApi.csproj"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..\\src")

function Invoke-Migration {
    param(
        [string]$Context,
        [string]$ProjectPath,
        [string]$Name
    )

    Write-Host "Adding migration '$Name' for $Context..."
    dotnet ef migrations add $Name `
        -c $Context `
        -p (Join-Path $root $ProjectPath) `
        -s (Join-Path $root $Startup) `
        -o "Migrations"
}

Invoke-Migration -Context "UserDbContext" -ProjectPath "modules\\User\\Infrastructure\\User.Infrastructure.csproj" -Name "Initial_User"
Invoke-Migration -Context "ProductDbContext" -ProjectPath "modules\\Product\\Infrastructure\\Product.Infrastructure.csproj" -Name "Initial_Product"
Invoke-Migration -Context "OrderDbContext" -ProjectPath "modules\\Order\\Infrastructure\\Order.Infrastructure.csproj" -Name "Initial_Order"
