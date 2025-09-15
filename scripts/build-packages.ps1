# LMSupplyDepots Package Build Script
# Builds and packages all distributable components

param(
    [string]$Configuration = "Release",
    [string]$VersionSuffix = "",
    [string]$OutputPath = ".\artifacts",
    [switch]$SkipTests,
    [switch]$Pack,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 LMSupplyDepots Package Builder" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green
Write-Host ""

# Configuration
$SolutionPath = "src\LMSupplyDepots.sln"
$PackageOutputPath = Join-Path $OutputPath "packages"
$PublishOutputPath = Join-Path $OutputPath "publish"

# Create output directories
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
New-Item -ItemType Directory -Path $PackageOutputPath -Force | Out-Null
New-Item -ItemType Directory -Path $PublishOutputPath -Force | Out-Null

Write-Host "📁 Output Directory: $OutputPath" -ForegroundColor Cyan
Write-Host "📦 Package Directory: $PackageOutputPath" -ForegroundColor Cyan
Write-Host "🚢 Publish Directory: $PublishOutputPath" -ForegroundColor Cyan
Write-Host ""

# Version information
$Version = "1.0.0"
if ($VersionSuffix) {
    $FullVersion = "$Version-$VersionSuffix"
} else {
    $FullVersion = $Version
}

Write-Host "🏷️  Version: $FullVersion" -ForegroundColor Yellow
Write-Host "⚙️  Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

# Restore dependencies
Write-Host "📥 Restoring dependencies..." -ForegroundColor Cyan
dotnet restore $SolutionPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Restore failed" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Dependencies restored" -ForegroundColor Green
Write-Host ""

# Build solution
Write-Host "🔨 Building solution..." -ForegroundColor Cyan
$buildArgs = @(
    "build", $SolutionPath
    "--configuration", $Configuration
    "--no-restore"
    "-p:Version=$FullVersion"
    "-p:AssemblyVersion=$Version"
    "-p:FileVersion=$Version"
    "-p:InformationalVersion=$FullVersion"
)

& dotnet $buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build successful" -ForegroundColor Green
Write-Host ""

# Run tests
if (-not $SkipTests) {
    Write-Host "🧪 Running tests..." -ForegroundColor Cyan
    $testArgs = @(
        "test", $SolutionPath
        "--configuration", $Configuration
        "--no-build"
        "--logger", "trx"
        "--results-directory", (Join-Path $OutputPath "TestResults")
        "--filter", "Category!=RequiresModel&Category!=RequiresApiKey&Category!=RequiresNetwork&Category!=RequiresLargeMemory&Category!=RequiresGpu&Category!=LocalOnly"
    )

    & dotnet $testArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Tests failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ All tests passed" -ForegroundColor Green
    Write-Host ""
}

# Package projects
if ($Pack) {
    Write-Host "📦 Creating NuGet packages..." -ForegroundColor Cyan

    $PackageProjects = @(
        "src\LMSupplyDepots\LMSupplyDepots.csproj",
        "src\LMSupplyDepots.SDK\LMSupplyDepots.SDK.csproj",
        "src\LMSupplyDepots.Inference\LMSupplyDepots.Inference.csproj",
        "src\LMSupplyDepots.ModelHub\LMSupplyDepots.ModelHub.csproj",
        "src\LMSupplyDepots.External.OpenAI\LMSupplyDepots.External.OpenAI.csproj",
        "src\LMSupplyDepots.External.LLamaEngine\LMSupplyDepots.External.LLamaEngine.csproj",
        "src\LMSupplyDepots.External.HuggingFace\LMSupplyDepots.External.HuggingFace.csproj",
        "src\LMSupplyDepots.CLI\LMSupplyDepots.CLI.csproj"
    )

    foreach ($project in $PackageProjects) {
        if (Test-Path $project) {
            Write-Host "  📦 Packaging: $(Split-Path $project -Leaf)" -ForegroundColor Gray

            $packArgs = @(
                "pack", $project
                "--configuration", $Configuration
                "--no-build"
                "--output", $PackageOutputPath
                "-p:PackageVersion=$FullVersion"
                "-p:Version=$FullVersion"
            )

            & dotnet $packArgs
            if ($LASTEXITCODE -ne 0) {
                Write-Host "    ❌ Failed to package $project" -ForegroundColor Red
                continue
            }
            Write-Host "    ✅ Packaged successfully" -ForegroundColor Green
        }
    }

    Write-Host ""
    Write-Host "📊 Package Summary:" -ForegroundColor Cyan
    $packages = Get-ChildItem -Path $PackageOutputPath -Filter "*.nupkg"
    foreach ($package in $packages) {
        $size = [math]::Round($package.Length / 1KB, 2)
        Write-Host "  - $($package.Name) ($size KB)" -ForegroundColor Gray
    }
    Write-Host ""
}

# Publish applications
if ($Publish) {
    Write-Host "🚢 Publishing applications..." -ForegroundColor Cyan

    # Publish HostApp (Web Application)
    Write-Host "  🌐 Publishing HostApp..." -ForegroundColor Gray
    $publishArgs = @(
        "publish", "src\LMSupplyDepots.HostApp\LMSupplyDepots.HostApp.csproj"
        "--configuration", $Configuration
        "--no-build"
        "--output", (Join-Path $PublishOutputPath "hostapp")
        "--runtime", "linux-x64"
        "--self-contained", "false"
        "-p:PublishReadyToRun=true"
        "-p:PublishTrimmed=false"
    )

    & dotnet $publishArgs
    if ($LASTEXITCODE -eq 0) {
        Write-Host "    ✅ HostApp published" -ForegroundColor Green
    } else {
        Write-Host "    ❌ HostApp publish failed" -ForegroundColor Red
    }

    # Publish CLI (Command Line Tool)
    Write-Host "  💻 Publishing CLI..." -ForegroundColor Gray
    $publishArgs = @(
        "publish", "src\LMSupplyDepots.CLI\LMSupplyDepots.CLI.csproj"
        "--configuration", $Configuration
        "--no-build"
        "--output", (Join-Path $PublishOutputPath "cli-linux")
        "--runtime", "linux-x64"
        "--self-contained", "true"
        "-p:PublishSingleFile=true"
        "-p:PublishReadyToRun=true"
        "-p:PublishTrimmed=true"
        "-p:EnableCompressionInSingleFile=true"
    )

    & dotnet $publishArgs
    if ($LASTEXITCODE -eq 0) {
        Write-Host "    ✅ CLI (Linux) published" -ForegroundColor Green
    } else {
        Write-Host "    ❌ CLI (Linux) publish failed" -ForegroundColor Red
    }

    # Publish CLI for Windows
    Write-Host "  💻 Publishing CLI (Windows)..." -ForegroundColor Gray
    $publishArgs = @(
        "publish", "src\LMSupplyDepots.CLI\LMSupplyDepots.CLI.csproj"
        "--configuration", $Configuration
        "--no-build"
        "--output", (Join-Path $PublishOutputPath "cli-win")
        "--runtime", "win-x64"
        "--self-contained", "true"
        "-p:PublishSingleFile=true"
        "-p:PublishReadyToRun=true"
        "-p:PublishTrimmed=true"
        "-p:EnableCompressionInSingleFile=true"
    )

    & dotnet $publishArgs
    if ($LASTEXITCODE -eq 0) {
        Write-Host "    ✅ CLI (Windows) published" -ForegroundColor Green
    } else {
        Write-Host "    ❌ CLI (Windows) publish failed" -ForegroundColor Red
    }

    Write-Host ""
    Write-Host "📊 Published Applications:" -ForegroundColor Cyan

    # Show directory sizes
    $publishDirs = Get-ChildItem -Path $PublishOutputPath -Directory
    foreach ($dir in $publishDirs) {
        $size = (Get-ChildItem -Path $dir.FullName -Recurse -File | Measure-Object -Property Length -Sum).Sum
        $sizeMB = [math]::Round($size / 1MB, 2)
        Write-Host "  - $($dir.Name): $sizeMB MB" -ForegroundColor Gray

        # List main executables
        $executables = Get-ChildItem -Path $dir.FullName -Filter "*.exe" -Recurse
        $executables += Get-ChildItem -Path $dir.FullName -Filter "lmd" -Recurse
        foreach ($exe in $executables) {
            Write-Host "    → $($exe.Name)" -ForegroundColor DarkGray
        }
    }
    Write-Host ""
}

# Generate build report
Write-Host "📋 Generating build report..." -ForegroundColor Cyan
$reportPath = Join-Path $OutputPath "build-report.json"

$buildReport = @{
    Version = $FullVersion
    Configuration = $Configuration
    BuildTime = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
    Packages = @()
    Publications = @()
}

if ($Pack) {
    $packages = Get-ChildItem -Path $PackageOutputPath -Filter "*.nupkg"
    foreach ($package in $packages) {
        $buildReport.Packages += @{
            Name = $package.Name
            Size = $package.Length
            Path = $package.FullName
        }
    }
}

if ($Publish) {
    $publishDirs = Get-ChildItem -Path $PublishOutputPath -Directory
    foreach ($dir in $publishDirs) {
        $size = (Get-ChildItem -Path $dir.FullName -Recurse -File | Measure-Object -Property Length -Sum).Sum
        $buildReport.Publications += @{
            Name = $dir.Name
            Size = $size
            Path = $dir.FullName
        }
    }
}

$buildReport | ConvertTo-Json -Depth 3 | Out-File -FilePath $reportPath -Encoding UTF8
Write-Host "📄 Build report saved to: $reportPath" -ForegroundColor Gray
Write-Host ""

Write-Host "🎉 Build completed successfully!" -ForegroundColor Green
Write-Host "📁 Artifacts available in: $OutputPath" -ForegroundColor Cyan