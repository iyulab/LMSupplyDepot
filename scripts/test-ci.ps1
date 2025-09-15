# LMSupplyDepots CI Test Script
# This script runs tests suitable for CI/CD environments
# Excludes tests that require local resources, API keys, or large models

param(
    [string]$Configuration = "Release",
    [string]$Filter = "*",
    [switch]$Verbose
)

Write-Host "🏗️ LMSupplyDepots CI Test Runner" -ForegroundColor Green
Write-Host "===============================" -ForegroundColor Green

# Set test configuration
$env:ASPNETCORE_ENVIRONMENT = "Test"
$env:DOTNET_ENVIRONMENT = "Test"

# Test categories that can run in CI/CD
$CiTestCategories = @(
    "Unit",
    "Integration",
    "MockTests",
    "ConfigurationTests"
)

# Test categories to exclude from CI/CD
$ExcludedCategories = @(
    "RequiresModel",
    "RequiresApiKey",
    "RequiresNetwork",
    "RequiresLargeMemory",
    "RequiresGpu",
    "LocalOnly"
)

Write-Host "✅ Running test categories suitable for CI/CD:" -ForegroundColor Green
$CiTestCategories | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }

Write-Host ""
Write-Host "❌ Excluding categories that require local resources:" -ForegroundColor Red
$ExcludedCategories | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }

Write-Host ""

# Security scanning
Write-Host "🛡️ Running security scan..." -ForegroundColor Cyan
$auditResult = dotnet list package --vulnerable --include-transitive
if ($LASTEXITCODE -ne 0) {
    Write-Host "⚠️ Vulnerability scan completed with warnings" -ForegroundColor Yellow
}

# Check for known vulnerabilities
$vulnerabilityFound = $auditResult | Select-String -Pattern "has the following vulnerable packages"
if ($vulnerabilityFound) {
    Write-Host "🚨 SECURITY WARNING: Vulnerable packages detected!" -ForegroundColor Red
    $auditResult | Write-Host -ForegroundColor Gray
    Write-Host "Consider updating vulnerable packages before deployment" -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Host "✅ No known vulnerabilities found" -ForegroundColor Green
}

# Build solution
Write-Host "🔨 Building solution..." -ForegroundColor Cyan
$buildResult = dotnet build --configuration $Configuration --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build successful" -ForegroundColor Green

# Create exclusion filter
$ExclusionFilter = ($ExcludedCategories | ForEach-Object { "Category!=$_" }) -join "&"

# Create final filter
$FinalFilter = $ExclusionFilter
if ($Filter -ne "*") {
    $FinalFilter += "&$Filter"
}

Write-Host ""
Write-Host "🧪 Running CI tests..." -ForegroundColor Cyan

if ($Verbose) {
    Write-Host "Filter: $FinalFilter" -ForegroundColor Gray
}

# Run tests with coverage
$testCommand = @(
    "dotnet", "test"
    "--configuration", $Configuration
    "--no-build"
    "--logger", "trx"
    "--logger", "console;verbosity=normal"
    "--filter", "`"$FinalFilter`""
    "--collect", "XPlat Code Coverage"
    "--results-directory", "TestResults"
)

if ($Verbose) {
    Write-Host "Command: $($testCommand -join ' ')" -ForegroundColor Gray
}

& $testCommand[0] $testCommand[1..($testCommand.Length-1)]
$testExitCode = $LASTEXITCODE

Write-Host ""

if ($testExitCode -eq 0) {
    Write-Host "🎉 All CI tests passed!" -ForegroundColor Green

    # Look for coverage results
    $coverageFiles = Get-ChildItem -Path "TestResults" -Filter "*.xml" -Recurse | Where-Object { $_.Name -like "*coverage*" }
    if ($coverageFiles) {
        Write-Host "📊 Code coverage reports generated:" -ForegroundColor Cyan
        $coverageFiles | ForEach-Object { Write-Host "  - $($_.FullName)" -ForegroundColor Gray }
    }
} else {
    Write-Host "❌ Some CI tests failed" -ForegroundColor Red
}

exit $testExitCode