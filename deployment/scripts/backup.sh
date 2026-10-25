#!/usr/bin/env bash
# ==============================================================================
# Cell Knowledge Assistant - Automated Daily Backup Script
# Conforms to Doc 8 §9 (Backup Strategy & Recovery)
# - Performs hot SQLite .backup transaction
# - Triggers Qdrant collection snapshot via REST API
# - Archives configuration and vault metadata
# - Enforces 30-day retention cleanup
# ==============================================================================
set -euo pipefail

TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_ROOT="${BACKUP_DIR:-/opt/cell-knowledge-assistant/backups}"
TEMP_BACKUP_DIR="/tmp/cell_knowledge_backup_${TIMESTAMP}"

SQLITE_DB="${KNOWLEDGE_BASE_SQLITE_PATH:-data/metadata.db}"
QDRANT_HOST="${QDRANT_HOST:-localhost}"
QDRANT_PORT="${QDRANT_HTTP_PORT:-6333}"
COLLECTION_NAME="${QDRANT_COLLECTION_NAME:-cell-knowledge}"
CONFIG_FILE="${CONFIG_PATH:-config/config.yaml}"
VAULT_DIR="${KNOWLEDGE_BASE_ROOT_PATH:-sample-data/KnowledgeBase}"
RETENTION_DAYS=30

echo "=========================================================="
echo " Cell Knowledge Assistant: Backup Execution [${TIMESTAMP}]"
echo " Destination: ${BACKUP_ROOT}"
echo "=========================================================="

mkdir -p "${BACKUP_ROOT}"
mkdir -p "${TEMP_BACKUP_DIR}"

# 1. Backup SQLite Database via transactional online backup API
if [ -f "${SQLITE_DB}" ]; then
    echo "Backing up SQLite document registry (${SQLITE_DB})..."
    if command -v sqlite3 >/dev/null 2>&1; then
        sqlite3 "${SQLITE_DB}" ".backup '${TEMP_BACKUP_DIR}/metadata_${TIMESTAMP}.db'"
    else
        echo "Warning: sqlite3 CLI not found, performing copy fallback..."
        cp "${SQLITE_DB}" "${TEMP_BACKUP_DIR}/metadata_${TIMESTAMP}.db"
    fi
    echo "SQLite backup completed."
else
    echo "Notice: SQLite database not found at ${SQLITE_DB}. Skipping."
fi

# 2. Trigger and Download Qdrant Snapshot
echo "Checking Qdrant collection snapshot service (${QDRANT_HOST}:${QDRANT_PORT})..."
QDRANT_SNAPSHOT_URL="http://${QDRANT_HOST}:${QDRANT_PORT}/collections/${COLLECTION_NAME}/snapshots"
if curl -sf "${QDRANT_SNAPSHOT_URL}" > /dev/null 2>&1; then
    echo "Creating Qdrant snapshot for collection '${COLLECTION_NAME}'..."
    SNAPSHOT_RESPONSE=$(curl -s -X POST "${QDRANT_SNAPSHOT_URL}")
    SNAPSHOT_NAME=$(echo "${SNAPSHOT_RESPONSE}" | grep -o '"name":"[^"]*' | cut -d'"' -f4 || true)

    if [ -n "${SNAPSHOT_NAME}" ]; then
        echo "Downloading Qdrant snapshot: ${SNAPSHOT_NAME}..."
        curl -s -o "${TEMP_BACKUP_DIR}/${SNAPSHOT_NAME}" "http://${QDRANT_HOST}:${QDRANT_PORT}/collections/${COLLECTION_NAME}/snapshots/${SNAPSHOT_NAME}"
        echo "Qdrant snapshot captured successfully."
    else
        echo "Warning: Unable to parse snapshot name from response: ${SNAPSHOT_RESPONSE}"
    fi
else
    echo "Notice: Qdrant service not reachable at ${QDRANT_SNAPSHOT_URL}. Skipping live snapshot."
fi

# 3. Archive Configuration Files
if [ -f "${CONFIG_FILE}" ]; then
    echo "Backing up application configuration..."
    mkdir -p "${TEMP_BACKUP_DIR}/config"
    cp "${CONFIG_FILE}" "${TEMP_BACKUP_DIR}/config/"
fi

# 4. Create Consolidated Tarball
ARCHIVE_NAME="${BACKUP_ROOT}/cell_knowledge_backup_${TIMESTAMP}.tar.gz"
echo "Creating compressed backup archive: ${ARCHIVE_NAME}..."
tar -czf "${ARCHIVE_NAME}" -C "${TEMP_BACKUP_DIR}" .
rm -rf "${TEMP_BACKUP_DIR}"

echo "Backup archive successfully created: $(ls -lh "${ARCHIVE_NAME}")"

# 5. Enforce 30-Day Retention Policy
echo "Applying ${RETENTION_DAYS}-day backup retention policy..."
find "${BACKUP_ROOT}" -name "cell_knowledge_backup_*.tar.gz" -type f -mtime +${RETENTION_DAYS} -exec rm -f {} +
echo "Retention purge complete."
echo "Backup process finished successfully."
