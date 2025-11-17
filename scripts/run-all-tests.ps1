# ===================================================================
# LMSupplyDepots Test Runner - All Tests (Including LocalIntegration)
# ===================================================================
#
# This script runs ALL tests including LocalIntegration tests that
# require external API calls and are excluded from CICD pipelines.
#
# Usage:
#   .\scripts\run-all-tests.ps1 [-Configuration Release|Debug]
#
# Environment Variables:
#   HUGGINGFACE_API_TOKEN - Optional API token for integration tests
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
Write-Host "LMSupplyDepots - ALL TESTS (Including LocalIntegration)" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Build: $(if ($NoBuild) { 'Skipped' } else { 'Enabled' })" -ForegroundColor Yellow
Write-Host "Verbosity: $(if ($DetailedOutput) { 'Detailed' } else { 'Normal' })" -ForegroundColor Yellow
Write-Host ""

# Check for HuggingFace API token
if ([string]::IsNullOrEmpty($env:HUGGINGFACE_API_TOKEN)) {
    Write-Host "⚠️  WARNING: HUGGINGFACE_API_TOKEN not set" -ForegroundColor Yellow
    Write-Host "   LocalIntegration tests will use public models only" -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Host "✓ HuggingFace API Token configured" -ForegroundColor Green
    Write-Host ""
}

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

# Run tests
Write-Host "Running ALL tests (Unit + Integration + LocalIntegration)..." -ForegroundColor Cyan
Write-Host ""

$testArgs = @(
    "test"
    "$rootDir\src\LMSupplyDepots.sln"
    "--configuration", $Configuration
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
    Write-Host "✓ ALL TESTS PASSED" -ForegroundColor Green
} else {
    Write-Host "❌ SOME TESTS FAILED" -ForegroundColor Red
}
Write-Host "================================================" -ForegroundColor Cyan

exit $testExitCode
