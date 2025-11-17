# ===================================================================
# LMSupplyDepots Test Runner - CICD Tests Only
# ===================================================================
#
# This script runs only fast tests suitable for CICD pipelines.
# Excludes LocalIntegration tests that require external API calls.
#
# Usage:
#   .\scripts\run-cicd-tests.ps1 [-Configuration Release|Debug]
#
# Test Categories:
#   ✓ Unit - Fast unit tests with no external dependencies
#   ✓ Integration - Tests with local dependencies only
#   ✗ LocalIntegration - Excluded (real API calls, slow)
# ===================================================================

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [Parameter(Mandatory=$false)]
    [switch]$NoBuild,

    [Parameter(Mandatory=$false)]
    [switch]$DetailedOutput
)

$ErrorActionPreference = "Stop"
$rootDir = Split-Path -Parent $PSScriptRoot

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "LMSupplyDepots - CICD TESTS (Fast)" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Build: $(if ($NoBuild) { 'Skipped' } else { 'Enabled' })" -ForegroundColor Yellow
Write-Host "Filter: Category!=LocalIntegration" -ForegroundColor Yellow
Write-Host "Verbosity: $(if ($DetailedOutput) { 'Detailed' } else { 'Normal' })" -ForegroundColor Yellow
Write-Host ""

# Build solution if requested
if (-not $NoBuild) {
    Write-Host "Building solution..." -ForegroundColor Cyan
    Push-Location "$rootDir\src"
    try {
        dotnet build LMSupplyDepots.sln --configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Build failed" -ForegroundColor Red
            exit 1
        }
        Write-Host "✓ Build successful" -ForegroundColor Green
        Write-Host ""
    } finally {
        Pop-Location
    }
}

# Run CICD tests (excluding LocalIntegration)
Write-Host "Running CICD tests (Unit + Integration)..." -ForegroundColor Cyan
Write-Host ""

$testArgs = @(
    "test"
    "$rootDir\src\LMSupplyDepots.sln"
    "--configuration", $Configuration
    "--filter", "Category!=LocalIntegration"
    "--logger", "console;verbosity=normal"
)

if ($NoBuild) {
    $testArgs += "--no-build"
}

if ($DetailedOutput) {
    $testArgs += "--verbosity", "detailed"
}

Push-Location "$rootDir\src"
try {
    & dotnet $testArgs
    $testExitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
if ($testExitCode -eq 0) {
    Write-Host "✓ ALL CICD TESTS PASSED" -ForegroundColor Green
} else {
    Write-Host "❌ SOME TESTS FAILED" -ForegroundColor Red
}
Write-Host "================================================" -ForegroundColor Cyan

exit $testExitCode
