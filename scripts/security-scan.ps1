# LMSupplyDepots Security Scanner
# Comprehensive security analysis for NuGet packages and dependencies

param(
    [switch]$Detailed,
    [switch]$OnlyVulnerable,
    [string]$OutputFormat = "text"
)

Write-Host "🛡️ LMSupplyDepots Security Scanner" -ForegroundColor Green
Write-Host "==============================" -ForegroundColor Green

# Set environment for security scanning
$env:NUGET_AUDIT = "true"
$env:NUGET_AUDIT_LEVEL = "low"

Write-Host ""
Write-Host "🔍 Checking for vulnerable packages..." -ForegroundColor Cyan

# Get vulnerability report
$vulnerabilityReport = dotnet list package --vulnerable --include-transitive
$reportExitCode = $LASTEXITCODE

if ($reportExitCode -eq 0 -and $vulnerabilityReport -notlike "*has the following vulnerable packages*") {
    Write-Host "✅ No vulnerable packages found" -ForegroundColor Green
} else {
    Write-Host "⚠️ Vulnerable packages detected:" -ForegroundColor Yellow
    $vulnerabilityReport | Write-Host -ForegroundColor Gray
}

Write-Host ""
Write-Host "📊 Package audit summary..." -ForegroundColor Cyan

# Run detailed package analysis
$auditResults = @()

# Get all projects
$projects = Get-ChildItem -Path "." -Filter "*.csproj" -Recurse

foreach ($project in $projects) {
    Write-Host "  Analyzing: $($project.Name)" -ForegroundColor Gray

    Push-Location -Path $project.Directory

    try {
        # Get package references
        $packages = dotnet list package --include-transitive --format json | ConvertFrom-Json

        if ($packages.projects) {
            foreach ($proj in $packages.projects) {
                foreach ($framework in $proj.frameworks) {
                    foreach ($package in $framework.topLevelPackages) {
                        $auditResults += [PSCustomObject]@{
                            Project = $project.Name
                            Package = $package.id
                            Version = $package.resolvedVersion
                            Framework = $framework.framework
                            Type = "Direct"
                        }
                    }

                    if ($framework.transitivePackages) {
                        foreach ($package in $framework.transitivePackages) {
                            $auditResults += [PSCustomObject]@{
                                Project = $project.Name
                                Package = $package.id
                                Version = $package.resolvedVersion
                                Framework = $framework.framework
                                Type = "Transitive"
                            }
                        }
                    }
                }
            }
        }
    } catch {
        Write-Host "    ❌ Error analyzing $($project.Name): $($_.Exception.Message)" -ForegroundColor Red
    }

    Pop-Location
}

# Generate security report
Write-Host ""
Write-Host "📋 Security Analysis Report" -ForegroundColor Green
Write-Host "===========================" -ForegroundColor Green

$totalPackages = $auditResults.Count
$uniquePackages = ($auditResults | Group-Object Package).Count
$directPackages = ($auditResults | Where-Object { $_.Type -eq "Direct" }).Count
$transitivePackages = ($auditResults | Where-Object { $_.Type -eq "Transitive" }).Count

Write-Host "Total package references: $totalPackages" -ForegroundColor Cyan
Write-Host "Unique packages: $uniquePackages" -ForegroundColor Cyan
Write-Host "Direct dependencies: $directPackages" -ForegroundColor Cyan
Write-Host "Transitive dependencies: $transitivePackages" -ForegroundColor Cyan

if ($Detailed) {
    Write-Host ""
    Write-Host "📦 Package breakdown by project:" -ForegroundColor Yellow

    $auditResults | Group-Object Project | ForEach-Object {
        Write-Host "  $($_.Name): $($_.Count) packages" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "🏷️ Most common packages:" -ForegroundColor Yellow

    $auditResults | Group-Object Package | Sort-Object Count -Descending | Select-Object -First 10 | ForEach-Object {
        Write-Host "  $($_.Name): used in $($_.Count) contexts" -ForegroundColor Gray
    }
}

# Check for security best practices
Write-Host ""
Write-Host "🔒 Security Best Practices Check" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green

# Check for Directory.Build.props
if (Test-Path "Directory.Build.props") {
    Write-Host "✅ Directory.Build.props found (centralized security configuration)" -ForegroundColor Green
} else {
    Write-Host "⚠️ Directory.Build.props missing (consider adding for security settings)" -ForegroundColor Yellow
}

# Check for Directory.Packages.props
if (Test-Path "Directory.Packages.props") {
    Write-Host "✅ Directory.Packages.props found (centralized package management)" -ForegroundColor Green
} else {
    Write-Host "⚠️ Directory.Packages.props missing (consider for dependency management)" -ForegroundColor Yellow
}

# Check for security config
if (Test-Path ".config\nuget-security.config") {
    Write-Host "✅ NuGet security configuration found" -ForegroundColor Green
} else {
    Write-Host "⚠️ NuGet security configuration missing" -ForegroundColor Yellow
}

# Generate recommendations
Write-Host ""
Write-Host "💡 Security Recommendations" -ForegroundColor Green
Write-Host "===========================" -ForegroundColor Green

Write-Host "• Run 'dotnet list package --outdated' regularly to update packages" -ForegroundColor Cyan
Write-Host "• Enable NuGetAudit in all project files for continuous scanning" -ForegroundColor Cyan
Write-Host "• Configure Dependabot for automated security updates" -ForegroundColor Cyan
Write-Host "• Consider using package vulnerability database integration" -ForegroundColor Cyan
Write-Host "• Implement security policies for package approval" -ForegroundColor Cyan

# Output results based on format
if ($OutputFormat -eq "json" -and $auditResults.Count -gt 0) {
    $jsonPath = "security-audit-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
    $auditResults | ConvertTo-Json -Depth 3 | Out-File -FilePath $jsonPath -Encoding UTF8
    Write-Host ""
    Write-Host "📄 JSON report saved to: $jsonPath" -ForegroundColor Green
}

Write-Host ""
if ($reportExitCode -eq 0 -and $vulnerabilityReport -notlike "*has the following vulnerable packages*") {
    Write-Host "🎉 Security scan completed - No vulnerabilities found!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "⚠️ Security scan completed - Review vulnerabilities above" -ForegroundColor Yellow
    exit 1
}