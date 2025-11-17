# LMSupplyDepots Local Test Script
# This script runs tests that require local resources (models, API keys, etc.)
# and cannot be executed in CI/CD environments

param(
    [string]$Filter = "*",
    [switch]$Verbose,
    [switch]$SkipModelDownload
)

Write-Host "🧪 LMSupplyDepots Local Test Runner" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

# Set test configuration
$env:ASPNETCORE_ENVIRONMENT = "Test"
$env:DOTNET_ENVIRONMENT = "Test"

# Test categories that require local resources
$LocalTestCategories = @(
    "RequiresModel",
    "RequiresApiKey",
    "RequiresNetwork",
    "RequiresLargeMemory",
    "RequiresGpu",
    "Integration"
)

Write-Host "📋 Running tests in the following categories:" -ForegroundColor Yellow
$LocalTestCategories | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
Write-Host ""

# Check prerequisites
Write-Host "🔍 Checking prerequisites..." -ForegroundColor Cyan

# Check for required environment variables
$RequiredEnvVars = @(
    "OPENAI_API_KEY",
    "HUGGINGFACE_API_TOKEN"
)

$MissingEnvVars = @()
foreach ($envVar in $RequiredEnvVars) {
    if (-not [System.Environment]::GetEnvironmentVariable($envVar)) {
        $MissingEnvVars += $envVar
    }
}

if ($MissingEnvVars.Count -gt 0) {
    Write-Host "⚠️  Missing environment variables:" -ForegroundColor Yellow
    $MissingEnvVars | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host "  Tests requiring API keys will be skipped" -ForegroundColor Yellow
    Write-Host ""
}

# Check model directory
$ModelDir = "D:\data\LMSupplyDepot\models"
if (-not (Test-Path $ModelDir)) {
    Write-Host "📁 Creating model directory: $ModelDir" -ForegroundColor Cyan
    New-Item -Path $ModelDir -ItemType Directory -Force
}

# Download test models if not skipped
if (-not $SkipModelDownload) {
    Write-Host "📦 Checking for test models..." -ForegroundColor Cyan

    $TestModels = @(
        @{
            Name = "TinyLlama-1.1B-Chat-v1.0.Q4_K_M.gguf"
            Url = "https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/resolve/main/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf"
            Size = "669MB"
        }
    )

    foreach ($model in $TestModels) {
        $modelPath = Join-Path $ModelDir $model.Name
        if (-not (Test-Path $modelPath)) {
            Write-Host "  📥 Downloading $($model.Name) ($($model.Size))..." -ForegroundColor Yellow
            Write-Host "     This may take several minutes..." -ForegroundColor Gray
            try {
                Invoke-WebRequest -Uri $model.Url -OutFile $modelPath -UseBasicParsing
                Write-Host "  ✅ Downloaded successfully" -ForegroundColor Green
            } catch {
                Write-Host "  ❌ Download failed: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host "     Model-dependent tests will be skipped" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  ✅ $($model.Name) already exists" -ForegroundColor Green
        }
    }
} else {
    Write-Host "⏭️  Model download skipped" -ForegroundColor Yellow
}

Write-Host ""

# Build solution first
Write-Host "🔨 Building solution..." -ForegroundColor Cyan
$buildResult = dotnet build src/LMSupplyDepots.sln --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build successful" -ForegroundColor Green
Write-Host ""

# Run local tests by category
Write-Host "🧪 Running local tests..." -ForegroundColor Cyan

$TestResults = @()
foreach ($category in $LocalTestCategories) {
    Write-Host "  🔍 Testing category: $category" -ForegroundColor Yellow

    $categoryFilter = "Category=$category"
    if ($Filter -ne "*") {
        $categoryFilter += "&$Filter"
    }

    $testCommand = "dotnet test src/LMSupplyDepots.sln --configuration Release --no-build --logger `"console;verbosity=normal`" --filter `"$categoryFilter`""

    if ($Verbose) {
        Write-Host "    Command: $testCommand" -ForegroundColor Gray
    }

    $testOutput = Invoke-Expression $testCommand
    $testExitCode = $LASTEXITCODE

    $TestResults += @{
        Category = $category
        ExitCode = $testExitCode
        Success = ($testExitCode -eq 0)
    }

    if ($testExitCode -eq 0) {
        Write-Host "    ✅ $category tests passed" -ForegroundColor Green
    } else {
        Write-Host "    ❌ $category tests failed" -ForegroundColor Red
    }
}

Write-Host ""

# Generate test report
Write-Host "📊 Test Results Summary" -ForegroundColor Green
Write-Host "======================" -ForegroundColor Green

$PassedTests = ($TestResults | Where-Object { $_.Success }).Count
$FailedTests = ($TestResults | Where-Object { -not $_.Success }).Count
$TotalTests = $TestResults.Count

Write-Host "Total Categories: $TotalTests" -ForegroundColor Cyan
Write-Host "Passed: $PassedTests" -ForegroundColor Green
Write-Host "Failed: $FailedTests" -ForegroundColor Red

if ($FailedTests -eq 0) {
    Write-Host ""
    Write-Host "🎉 All local tests passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "❌ Some tests failed. Check the output above for details." -ForegroundColor Red
    Write-Host "Failed categories:" -ForegroundColor Yellow
    $TestResults | Where-Object { -not $_.Success } | ForEach-Object {
        Write-Host "  - $($_.Category)" -ForegroundColor Red
    }
    exit 1
}