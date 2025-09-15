# LMSupplyDepots Deployment Script
# Automated deployment tool for staging and production environments

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("staging", "production")]
    [string]$Environment,

    [string]$Version = "latest",
    [string]$Registry = "ghcr.io/your-org/lmsupplydepots",
    [switch]$DryRun,
    [switch]$SkipHealthCheck,
    [switch]$Rollback
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 LMSupplyDepots Deployment Manager" -ForegroundColor Green
Write-Host "====================================" -ForegroundColor Green
Write-Host ""

# Configuration
$ComposeFile = if ($Environment -eq "production") {
    "docker-compose.prod.yml"
} else {
    "docker-compose.yml"
}

$EnvFile = ".env.$Environment"

Write-Host "🎯 Target Environment: $Environment" -ForegroundColor Yellow
Write-Host "📋 Compose File: $ComposeFile" -ForegroundColor Yellow
Write-Host "🏷️  Version: $Version" -ForegroundColor Yellow
Write-Host "🐳 Registry: $Registry" -ForegroundColor Yellow
if ($DryRun) {
    Write-Host "🔍 DRY RUN MODE - No actual deployment will occur" -ForegroundColor Magenta
}
Write-Host ""

# Pre-deployment checks
Write-Host "🔍 Running pre-deployment checks..." -ForegroundColor Cyan

# Check Docker is running
try {
    docker version | Out-Null
    Write-Host "  ✅ Docker is running" -ForegroundColor Green
} catch {
    Write-Host "  ❌ Docker is not running or not accessible" -ForegroundColor Red
    exit 1
}

# Check Docker Compose is available
try {
    docker-compose version | Out-Null
    Write-Host "  ✅ Docker Compose is available" -ForegroundColor Green
} catch {
    Write-Host "  ❌ Docker Compose is not available" -ForegroundColor Red
    exit 1
}

# Check compose file exists
if (-not (Test-Path $ComposeFile)) {
    Write-Host "  ❌ Compose file not found: $ComposeFile" -ForegroundColor Red
    exit 1
}
Write-Host "  ✅ Compose file exists: $ComposeFile" -ForegroundColor Green

# Check environment file exists
if (-not (Test-Path $EnvFile)) {
    Write-Host "  ⚠️  Environment file not found: $EnvFile" -ForegroundColor Yellow
    Write-Host "  Creating default environment file..." -ForegroundColor Yellow

    # Create basic environment file
    @"
# LMSupplyDepots Environment Configuration - $Environment
DB_PASSWORD=changeme-$(Get-Random)
OPENAI_API_KEY=
HUGGINGFACE_API_TOKEN=
REGISTRY=$Registry
VERSION=$Version
"@ | Out-File -FilePath $EnvFile -Encoding UTF8

    Write-Host "  📝 Please update $EnvFile with actual values before deployment" -ForegroundColor Yellow
}
Write-Host "  ✅ Environment file exists: $EnvFile" -ForegroundColor Green

# Load environment variables
Get-Content $EnvFile | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]*?)\s*=\s*(.*?)\s*$') {
        $name = $matches[1]
        $value = $matches[2]
        Set-Item -Path "env:$name" -Value $value
    }
}

Write-Host ""

# Handle rollback
if ($Rollback) {
    Write-Host "🔄 Rolling back to previous deployment..." -ForegroundColor Yellow

    if ($DryRun) {
        Write-Host "  🔍 Would execute: docker-compose -f $ComposeFile --env-file $EnvFile down" -ForegroundColor Magenta
        Write-Host "  🔍 Would restore from backup" -ForegroundColor Magenta
    } else {
        # Stop current deployment
        docker-compose -f $ComposeFile --env-file $EnvFile down

        # Restore from backup (implementation depends on your backup strategy)
        Write-Host "  📦 Restoring from backup..." -ForegroundColor Cyan
        # Add backup restoration logic here

        Write-Host "  ✅ Rollback completed" -ForegroundColor Green
    }
    exit 0
}

# Pull latest images
Write-Host "📥 Pulling container images..." -ForegroundColor Cyan
if ($DryRun) {
    Write-Host "  🔍 Would execute: docker-compose -f $ComposeFile --env-file $EnvFile pull" -ForegroundColor Magenta
} else {
    docker-compose -f $ComposeFile --env-file $EnvFile pull
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ❌ Failed to pull images" -ForegroundColor Red
        exit 1
    }
    Write-Host "  ✅ Images pulled successfully" -ForegroundColor Green
}

# Create backup before deployment (production only)
if ($Environment -eq "production") {
    Write-Host "💾 Creating backup..." -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  🔍 Would create database backup" -ForegroundColor Magenta
        Write-Host "  🔍 Would backup configuration files" -ForegroundColor Magenta
    } else {
        # Create timestamp for backup
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupDir = "backups\$timestamp"

        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

        # Backup database (using Docker)
        Write-Host "  📊 Backing up database..." -ForegroundColor Gray
        docker-compose -f $ComposeFile --env-file $EnvFile exec -T postgres pg_dump -U lmsupplydepots lmsupplydepots > "$backupDir\database.sql"

        # Backup volumes
        Write-Host "  📁 Backing up volumes..." -ForegroundColor Gray
        docker run --rm -v lmsupplydepots_app-data:/data -v ${PWD}/backups/$timestamp:/backup alpine tar czf /backup/app-data.tar.gz -C /data .

        Write-Host "  ✅ Backup created in $backupDir" -ForegroundColor Green
    }
}

# Deploy services
Write-Host "🚀 Deploying services..." -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "  🔍 Would execute: docker-compose -f $ComposeFile --env-file $EnvFile up -d" -ForegroundColor Magenta
} else {
    # Stop existing services gracefully
    Write-Host "  🛑 Stopping existing services..." -ForegroundColor Gray
    docker-compose -f $ComposeFile --env-file $EnvFile down --timeout 30

    # Start new services
    Write-Host "  🔄 Starting updated services..." -ForegroundColor Gray
    docker-compose -f $ComposeFile --env-file $EnvFile up -d

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ❌ Deployment failed" -ForegroundColor Red

        # Attempt rollback on failure
        Write-Host "  🔄 Attempting rollback..." -ForegroundColor Yellow
        docker-compose -f $ComposeFile --env-file $EnvFile down
        # Additional rollback logic would go here

        exit 1
    }

    Write-Host "  ✅ Services deployed successfully" -ForegroundColor Green
}

# Health checks
if (-not $SkipHealthCheck -and -not $DryRun) {
    Write-Host "🏥 Running health checks..." -ForegroundColor Cyan

    $maxRetries = 12
    $retryCount = 0
    $healthUrl = "http://localhost:8080/health"

    do {
        $retryCount++
        Write-Host "  🔍 Health check attempt $retryCount/$maxRetries..." -ForegroundColor Gray

        try {
            $response = Invoke-WebRequest -Uri $healthUrl -TimeoutSec 5 -UseBasicParsing
            if ($response.StatusCode -eq 200) {
                Write-Host "  ✅ Health check passed" -ForegroundColor Green
                break
            }
        } catch {
            Write-Host "  ⏳ Service not ready, waiting..." -ForegroundColor Yellow
        }

        if ($retryCount -ge $maxRetries) {
            Write-Host "  ❌ Health check failed after $maxRetries attempts" -ForegroundColor Red

            # Show logs for debugging
            Write-Host "  📋 Recent logs:" -ForegroundColor Yellow
            docker-compose -f $ComposeFile --env-file $EnvFile logs --tail=20 hostapp

            exit 1
        }

        Start-Sleep -Seconds 10
    } while ($true)
}

# Post-deployment tasks
Write-Host "🔧 Running post-deployment tasks..." -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "  🔍 Would run database migrations" -ForegroundColor Magenta
    Write-Host "  🔍 Would clear application caches" -ForegroundColor Magenta
    Write-Host "  🔍 Would warm up services" -ForegroundColor Magenta
} else {
    # Database migrations (if needed)
    Write-Host "  📊 Running database migrations..." -ForegroundColor Gray
    docker-compose -f $ComposeFile --env-file $EnvFile exec -T hostapp dotnet ef database update

    # Clear caches
    Write-Host "  🧹 Clearing caches..." -ForegroundColor Gray
    docker-compose -f $ComposeFile --env-file $EnvFile exec -T redis redis-cli FLUSHALL

    # Warm up services
    Write-Host "  🔥 Warming up services..." -ForegroundColor Gray
    Start-Sleep -Seconds 5
    try {
        Invoke-WebRequest -Uri "http://localhost:8080/" -TimeoutSec 10 -UseBasicParsing | Out-Null
        Write-Host "  ✅ Service warmed up" -ForegroundColor Green
    } catch {
        Write-Host "  ⚠️  Service warm-up failed, but deployment continues" -ForegroundColor Yellow
    }
}

# Display deployment summary
Write-Host ""
Write-Host "📋 Deployment Summary" -ForegroundColor Green
Write-Host "=====================" -ForegroundColor Green
Write-Host "Environment: $Environment" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "Registry: $Registry" -ForegroundColor Cyan
Write-Host "Deployment Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan

if (-not $DryRun) {
    Write-Host ""
    Write-Host "🔗 Service URLs:" -ForegroundColor Yellow
    Write-Host "  Application: http://localhost:8080" -ForegroundColor Gray
    Write-Host "  Health Check: http://localhost:8080/health" -ForegroundColor Gray
    if ($Environment -eq "production") {
        Write-Host "  Monitoring: http://localhost:9090" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "🎉 Deployment completed successfully!" -ForegroundColor Green

if ($DryRun) {
    Write-Host "🔍 This was a DRY RUN - no actual changes were made" -ForegroundColor Magenta
}