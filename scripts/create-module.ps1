Param(
    [Parameter(Mandatory = $true)]
    [Alias("n")]
    [string]$Name,

    [Alias("f")]
    [string]$Framework = "net10.0",

    [Alias("s")]
    [switch]$AddToSolution
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Name)) {
    throw "Module name is required. Use -n <Name>."
}

if ($Name -notmatch '^[A-Za-z][A-Za-z0-9]*$') {
    throw "Module name must start with a letter and contain only letters or digits."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$srcPath = Join-Path $repoRoot "src"
$modulesRoot = Join-Path $srcPath "modules"

if (-not (Test-Path $modulesRoot)) {
    throw "Modules directory not found at $modulesRoot. Run this script from the repository with a src/modules folder."
}

$moduleRoot = Join-Path $modulesRoot $Name

if (Test-Path $moduleRoot) {
    throw "Module '$Name' already exists at $moduleRoot."
}

$projectDefinitions = @(
    @{ SubPath = "Application"; Template = "classlib"; ProjectName = "$Name.Application" },
    @{ SubPath = "Domain"; Template = "classlib"; ProjectName = "$Name.Domain" },
    @{ SubPath = "Infrastructure"; Template = "classlib"; ProjectName = "$Name.Infrastructure" },
    @{ SubPath = "IntegrationEvent"; Template = "classlib"; ProjectName = "$Name.IntegrationEvent" },
    @{ SubPath = "tests\$Name.UnitTest"; Template = "xunit"; ProjectName = "$Name.UnitTest" },
    @{ SubPath = "tests\$Name.IntegrationTest"; Template = "xunit"; ProjectName = "$Name.IntegrationTest" }
)

foreach ($proj in $projectDefinitions) {
    $outputPath = Join-Path $moduleRoot $proj.SubPath
    Write-Host "Creating $($proj.ProjectName) with template '$($proj.Template)' at $outputPath (framework $Framework)..."
    dotnet new $proj.Template -n $proj.ProjectName -o $outputPath -f $Framework
}

if ($AddToSolution) {
    $solutionPath = Join-Path $srcPath "ModularMonolith.slnx"

    if (-not (Test-Path $solutionPath)) {
        Write-Warning "Solution file not found at $solutionPath. Skipping solution update."
    }
    else {
        $projectPaths = $projectDefinitions | ForEach-Object {
            $projPath = Join-Path $moduleRoot $_.SubPath
            Join-Path $projPath "$($_.ProjectName).csproj"
        }

        Write-Host "Adding projects to solution $solutionPath..."
        dotnet sln $solutionPath add $projectPaths
    }
}

Write-Host "Module '$Name' scaffolded at $moduleRoot."
