#!/usr/bin/env bash
# Daily logical backup of the dedicated ModularPlatform Postgres container.
# Uses pg_dump custom format (-Fc, compressed + restorable with pg_restore), keeps the newest $RETENTION dumps.
#
# Install (root crontab) — runs 03:30 daily, logs alongside the dumps:
#   30 3 * * * /opt/modularplatform/docs/deploy/pg-backup.sh >> /opt/modularplatform/backups/backup.log 2>&1
#
# Restore a dump into the running container:
#   docker compose exec -T postgres pg_restore -U postgres -d modularplatform --clean --if-exists < dump.file
set -euo pipefail

COMPOSE_DIR=/opt/modularplatform
BACKUP_DIR="$COMPOSE_DIR/backups"
RETENTION=30

mkdir -p "$BACKUP_DIR"
cd "$COMPOSE_DIR"

TS=$(date -u +%Y%m%d-%H%M%S)
OUT="$BACKUP_DIR/modularplatform-$TS.dump"

# Dump from inside the container (custom format streams to stdout).
docker compose exec -T postgres pg_dump -U postgres -d modularplatform -Fc > "$OUT"

# Retention: delete all but the newest $RETENTION dumps.
ls -1t "$BACKUP_DIR"/modularplatform-*.dump 2>/dev/null | tail -n +$((RETENTION + 1)) | xargs -r rm -f

echo "$(date -u +%FT%TZ): backup OK -> $(basename "$OUT") ($(du -h "$OUT" | cut -f1)), $(ls "$BACKUP_DIR"/modularplatform-*.dump 2>/dev/null | wc -l) kept"
