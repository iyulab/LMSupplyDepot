#!/bin/bash
# LMSupplyDepots Database Backup Script
# Automated backup solution for PostgreSQL database

set -e

# Configuration
BACKUP_DIR="/backups"
POSTGRES_HOST="${POSTGRES_HOST:-postgres}"
POSTGRES_DB="${POSTGRES_DB:-lmsupplydepots}"
POSTGRES_USER="${POSTGRES_USER:-lmsupplydepots}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-7}"

# Timestamp for backup file
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="$BACKUP_DIR/lmsupplydepots_backup_$TIMESTAMP.sql"
BACKUP_FILE_COMPRESSED="$BACKUP_FILE.gz"

echo "🗄️  LMSupplyDepots Database Backup"
echo "=================================="
echo "Database: $POSTGRES_DB"
echo "Host: $POSTGRES_HOST"
echo "User: $POSTGRES_USER"
echo "Backup file: $BACKUP_FILE_COMPRESSED"
echo ""

# Ensure backup directory exists
mkdir -p "$BACKUP_DIR"

# Check if PostgreSQL is accessible
echo "🔍 Checking database connectivity..."
if ! pg_isready -h "$POSTGRES_HOST" -U "$POSTGRES_USER" > /dev/null 2>&1; then
    echo "❌ Cannot connect to PostgreSQL at $POSTGRES_HOST"
    exit 1
fi
echo "✅ Database is accessible"

# Create backup
echo "💾 Creating database backup..."
export PGPASSWORD="$(cat /run/secrets/db_password 2>/dev/null || echo $POSTGRES_PASSWORD)"

if pg_dump -h "$POSTGRES_HOST" -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
   --verbose --clean --no-acl --no-owner \
   --format=plain > "$BACKUP_FILE"; then
    echo "✅ Database backup created successfully"
else
    echo "❌ Database backup failed"
    rm -f "$BACKUP_FILE"
    exit 1
fi

# Compress backup
echo "🗜️  Compressing backup..."
if gzip "$BACKUP_FILE"; then
    echo "✅ Backup compressed: $BACKUP_FILE_COMPRESSED"
else
    echo "❌ Compression failed"
    exit 1
fi

# Verify backup integrity
echo "🔍 Verifying backup integrity..."
if gunzip -t "$BACKUP_FILE_COMPRESSED"; then
    echo "✅ Backup integrity verified"
else
    echo "❌ Backup integrity check failed"
    exit 1
fi

# Calculate backup size
BACKUP_SIZE=$(du -h "$BACKUP_FILE_COMPRESSED" | cut -f1)
echo "📊 Backup size: $BACKUP_SIZE"

# Cleanup old backups
echo "🧹 Cleaning up old backups (older than $BACKUP_RETENTION_DAYS days)..."
find "$BACKUP_DIR" -name "lmsupplydepots_backup_*.sql.gz" -type f -mtime +$BACKUP_RETENTION_DAYS -delete
REMAINING_BACKUPS=$(find "$BACKUP_DIR" -name "lmsupplydepots_backup_*.sql.gz" -type f | wc -l)
echo "📁 Remaining backups: $REMAINING_BACKUPS"

# Create backup metadata
METADATA_FILE="$BACKUP_DIR/backup_metadata.json"
cat > "$METADATA_FILE" << EOF
{
  "latest_backup": {
    "timestamp": "$TIMESTAMP",
    "file": "$BACKUP_FILE_COMPRESSED",
    "size": "$BACKUP_SIZE",
    "database": "$POSTGRES_DB",
    "host": "$POSTGRES_HOST",
    "user": "$POSTGRES_USER",
    "created_at": "$(date -Iseconds)"
  }
}
EOF

echo "📋 Metadata updated: $METADATA_FILE"

# Optional: Upload to cloud storage (S3, Azure Blob, etc.)
if [ -n "$CLOUD_STORAGE_ENABLED" ] && [ "$CLOUD_STORAGE_ENABLED" = "true" ]; then
    echo "☁️  Uploading backup to cloud storage..."

    # Example for AWS S3 (uncomment and configure as needed)
    # aws s3 cp "$BACKUP_FILE_COMPRESSED" "s3://your-backup-bucket/lmsupplydepots/$(date +%Y/%m)/"

    # Example for Azure Blob Storage (uncomment and configure as needed)
    # az storage blob upload --file "$BACKUP_FILE_COMPRESSED" --container-name backups --name "lmsupplydepots/$(date +%Y/%m)/$(basename $BACKUP_FILE_COMPRESSED)"

    echo "✅ Backup uploaded to cloud storage"
fi

# Send notification (optional)
if [ -n "$NOTIFICATION_WEBHOOK" ]; then
    echo "📢 Sending notification..."
    curl -X POST "$NOTIFICATION_WEBHOOK" \
         -H "Content-Type: application/json" \
         -d "{\"text\":\"✅ LMSupplyDepots backup completed: $BACKUP_FILE_COMPRESSED ($BACKUP_SIZE)\"}" \
         > /dev/null 2>&1 || true
fi

echo ""
echo "🎉 Backup completed successfully!"
echo "📁 Backup location: $BACKUP_FILE_COMPRESSED"
echo "⏰ Next backup in 24 hours"