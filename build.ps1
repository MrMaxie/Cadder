param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$CheckOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

function Write-Header([string]$text) {
    Write-Host ""
    Write-Host "=== $text ===" -ForegroundColor Cyan
}

function Write-Info([string]$text) {
    Write-Host "  $text" -ForegroundColor Gray
}

function Fail([string]$message) {
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

$projects = @(
    "src\Cadder.Contracts\Cadder.Contracts.csproj",
    "src\Cadder.Daemon\Cadder.Daemon.csproj",
    "src\Cadder.CaddyShim\Cadder.CaddyShim.csproj",
    "src\Cadder.Tray.WinUI\Cadder.Tray.WinUI.csproj",
    "tests\Cadder.Contracts.Tests\Cadder.Contracts.Tests.csproj",
    "tests\Cadder.Daemon.Tests\Cadder.Daemon.Tests.csproj"
)

function Assert-SolutionProjectListMatchesBuildScript {
    [xml]$solution = Get-Content -LiteralPath (Join-Path $repoRoot "Cadder.slnx") -Raw

    $solutionProjects = @(
        $solution.SelectNodes("//Project") |
            ForEach-Object { $_.GetAttribute("Path").Replace("/", "\") } |
            Sort-Object
    )

    $scriptProjects = @(
        $projects |
            ForEach-Object { $_.Replace("/", "\") } |
            Sort-Object
    )

    $missingFromScript = @(
        Compare-Object -ReferenceObject $solutionProjects -DifferenceObject $scriptProjects |
            Where-Object SideIndicator -eq "<=" |
            Select-Object -ExpandProperty InputObject
    )

    $extraInScript = @(
        Compare-Object -ReferenceObject $solutionProjects -DifferenceObject $scriptProjects |
            Where-Object SideIndicator -eq "=>" |
            Select-Object -ExpandProperty InputObject
    )

    if ($missingFromScript.Count -gt 0 -or $extraInScript.Count -gt 0) {
        $message = "build.ps1 project list must match Cadder.slnx."
        if ($missingFromScript.Count -gt 0) {
            $message += " Missing from build.ps1: $($missingFromScript -join ',')."
        }
        if ($extraInScript.Count -gt 0) {
            $message += " Extra in build.ps1: $($extraInScript -join ',')."
        }

        Fail $message
    }
}

Write-Header "Checking prerequisites"

if ($env:OS -ne "Windows_NT") {
    Fail "Cadder scaffold builds require Windows."
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Fail ".NET SDK is not available on PATH."
}

$activeSdk = & dotnet --version
$sdks = & dotnet --list-sdks
if (-not ($sdks | Where-Object { $_ -match "^10\." })) {
    Fail ".NET 10 SDK is required. global.json pins SDK 10.0.204 with latestFeature roll-forward."
}

Write-Info ".NET SDK: $activeSdk"

$windowsSdkRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\Include"
if (-not (Test-Path -LiteralPath $windowsSdkRoot)) {
    Fail "Windows SDK include directory was not found. Install the Windows 10/11 SDK before building WinUI."
}

$windowsSdkVersions = Get-ChildItem -LiteralPath $windowsSdkRoot -Directory |
    Select-Object -ExpandProperty Name |
    Where-Object { $_ -match "^\d+\." } |
    Sort-Object -Descending

if (-not $windowsSdkVersions) {
    Fail "No Windows SDK versions were found under $windowsSdkRoot."
}

Write-Info "Windows SDK: $($windowsSdkVersions[0])"
Write-Info "Platform: x64"
Write-Info "RuntimeIdentifier: win-x64"

Assert-SolutionProjectListMatchesBuildScript

if ($CheckOnly) {
    Write-Host ""
    Write-Host "Prerequisite check completed."
    exit 0
}

Write-Header "Restoring"
& dotnet restore .\Cadder.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Header "Building"

foreach ($project in $projects) {
    Write-Info $project
    & dotnet build $project --no-restore -c $Configuration -p:Platform=x64 -p:RuntimeIdentifier=win-x64
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if ($project -eq "src\Cadder.CaddyShim\Cadder.CaddyShim.csproj") {
        $shimExe = Join-Path $repoRoot "src\Cadder.CaddyShim\bin\x64\$Configuration\net10.0\win-x64\caddy.exe"
        if (-not (Test-Path -LiteralPath $shimExe)) {
            Fail "Shim build did not produce expected PATH-facing executable: $shimExe"
        }
    }
}

Write-Host ""
Write-Host "Build completed."
