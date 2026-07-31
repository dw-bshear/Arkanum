#!/usr/bin/env bash
set -euo pipefail

ARKANUM_PACKAGE_VERSION=${ARKANUM_PACKAGE_VERSION:-2026.0.6}
ACTION=${ACTION:-auto}
RESTORE_SOURCE=${RESTORE_SOURCE:-dba-restored-database}
SQL_BACKUP_FILE=${SQL_BACKUP_FILE:-}
INSTALL_DIR=${INSTALL_DIR:-/opt/arkanum}
CONFIG_DIR=${CONFIG_DIR:-/etc/arkanum}
DATA_DIR=${DATA_DIR:-/var/lib/arkanum}
LOG_DIR=${LOG_DIR:-/var/log/arkanum}
BACKUP_ROOT=${BACKUP_ROOT:-/var/backups/arkanum}
SERVICE_USER=${SERVICE_USER:-arkanum}
APP_URLS=${APP_URLS:-http://127.0.0.1:5068}
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
PACKAGE_ROOT=$(cd "${SCRIPT_DIR}/../.." && pwd)
PACKAGE_DIR=${PACKAGE_DIR:-}
STATE_FILE="${CONFIG_DIR}/install-state.json"
REPORT_FILE="${LOG_DIR}/upgrade-report-${ARKANUM_PACKAGE_VERSION}.txt"

fail() {
  echo "[FAIL] $*" >&2
  exit 1
}

info() {
  echo "[INFO] $*"
}

write_report() {
  install -d -o "${SERVICE_USER}" -g "${SERVICE_USER}" -m 750 "${LOG_DIR}" 2>/dev/null || install -d -m 750 "${LOG_DIR}"
  local recovery_key_source="not provided"
  if [ -n "${ARKANUM_RESTORE_KEY_FILE:-}" ]; then
    recovery_key_source="file"
  elif [ -n "${ARKANUM_RESTORE_KEY:-}" ]; then
    recovery_key_source="pasted"
  fi
  local recovery_validation="${RECOVERY_VALIDATION:-NotRun}"
  {
    echo "Arkanum installer report"
    echo "Restore report"
    echo "Product: Arkanum"
    echo "Package version: ${ARKANUM_PACKAGE_VERSION}"
    echo "Install action: ${ACTION}"
    echo "Install directory: ${INSTALL_DIR}"
    echo "Config directory: ${CONFIG_DIR}"
    echo "Data directory: ${DATA_DIR}"
    echo "Log directory: ${LOG_DIR}"
    echo "State file: ${STATE_FILE}"
    echo "Generated UTC: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "Health URL: ${APP_URLS}/health"
    echo "Restore source: ${RESTORE_SOURCE}"
    echo "SQL backup file: ${SQL_BACKUP_FILE:-not provided}"
    echo "Recovery Key source: ${recovery_key_source}"
    echo "Recovery sentinel validation: ${recovery_validation}"
    echo "No SQL passwords or Recovery Key values are written to this report."
  } > "${REPORT_FILE}"
  chown "${SERVICE_USER}:${SERVICE_USER}" "${REPORT_FILE}" 2>/dev/null || true
  chmod 640 "${REPORT_FILE}" 2>/dev/null || true
}

if [ "${EUID}" -ne 0 ]; then
  fail "Run as root or with sudo."
fi

# Supported direct operator actions include ACTION=upgrade, ACTION=repair, and ACTION=restore.
case "${ACTION}" in
  auto|install|upgrade|repair|side-by-side|restore) ;;
  *) fail "ACTION must be one of auto, install, upgrade, repair, side-by-side, restore." ;;
esac
case "${RESTORE_SOURCE}" in
  dba-restored-database|sql-backup-file|attach-database-files) ;;
  *) fail "RESTORE_SOURCE must be one of dba-restored-database, sql-backup-file, attach-database-files." ;;
esac
# Restore from database backup example: ACTION=restore ARKANUM_RESTORE_KEY_FILE=/path/to/master-key.txt or ARKANUM_RESTORE_KEY=ark-recovery-v1....

if [ -z "${PACKAGE_DIR}" ]; then
  if [ -f "${PACKAGE_ROOT}/app/Arkanum.Web.dll" ]; then
    PACKAGE_DIR="${PACKAGE_ROOT}/app"
  elif [ -f "${PACKAGE_ROOT}/Arkanum.Web.dll" ]; then
    PACKAGE_DIR="${PACKAGE_ROOT}"
  else
    PACKAGE_DIR="${PACKAGE_ROOT}/app"
  fi
fi

command -v dotnet >/dev/null 2>&1 || fail "dotnet runtime was not found. Install the supported ASP.NET Core runtime before running this installer."
command -v rsync >/dev/null 2>&1 || fail "rsync was not found. Install rsync before running this installer."
command -v python3 >/dev/null 2>&1 || fail "python3 was not found. Install python3 before running this installer."
command -v systemctl >/dev/null 2>&1 || fail "systemctl was not found. This installer expects a systemd-based Linux host."

if [ ! -f "${PACKAGE_DIR}/Arkanum.Web.dll" ]; then
  fail "Published Arkanum.Web.dll was not found in PACKAGE_DIR=${PACKAGE_DIR}. Run from an Arkanum Linux package or set PACKAGE_DIR to the published app folder."
fi

if [ -z "${ARKANUM_SQL_CONNECTION:-}" ]; then
  cat >&2 <<'EOF'
[FAIL] ARKANUM_SQL_CONNECTION is required for Phase C installs and upgrades.

Example:
  sudo env \
    ARKANUM_SQL_CONNECTION='Server=tcp:sql01,1433;Database=Arkanum;User Id=arkanum_app;Password=...;Encrypt=True;TrustServerCertificate=True;' \
    ACTION=restore \
    ARKANUM_RESTORE_KEY_FILE=/path/to/master-key.txt \
    installers/linux/install-arkanum.sh
EOF
  exit 4
fi

EXISTING_INSTALL=false
if [ -s "${STATE_FILE}" ] || [ -f "${INSTALL_DIR}/Arkanum.Web.dll" ]; then
  EXISTING_INSTALL=true
fi

if [ "${ACTION}" = "auto" ]; then
  if [ "${EXISTING_INSTALL}" = true ]; then
    ACTION=upgrade
  else
    ACTION=install
  fi
fi

if [ "${ACTION}" = "restore" ]; then
  REPORT_FILE="${LOG_DIR}/restore-report-${ARKANUM_PACKAGE_VERSION}.txt"
fi

if [ "${ACTION}" = "side-by-side" ]; then
  INSTALL_DIR="${INSTALL_DIR}-${ARKANUM_PACKAGE_VERSION}"
  CONFIG_DIR="${CONFIG_DIR}-${ARKANUM_PACKAGE_VERSION}"
  DATA_DIR="${DATA_DIR}-${ARKANUM_PACKAGE_VERSION}"
  LOG_DIR="${LOG_DIR}-${ARKANUM_PACKAGE_VERSION}"
  STATE_FILE="${CONFIG_DIR}/install-state.json"
  REPORT_FILE="${LOG_DIR}/upgrade-report-${ARKANUM_PACKAGE_VERSION}.txt"
  ACTION=install
fi

if [ "${EXISTING_INSTALL}" = false ] && [ "${ACTION}" = "upgrade" ]; then
  fail "ACTION=upgrade requested but no existing Arkanum installation was detected. Use ACTION=install or ACTION=auto."
fi

if [ "${ACTION}" = "restore" ] && [ -z "${ARKANUM_RESTORE_KEY_FILE:-}" ] && [ -z "${ARKANUM_RESTORE_KEY:-}" ]; then
  fail "ACTION=restore requires ARKANUM_RESTORE_KEY_FILE or ARKANUM_RESTORE_KEY. Select Restore from database backup and provide the existing Arkanum Recovery Key."
fi

if [ "${ACTION}" = "restore" ] && [ "${RESTORE_SOURCE}" != "dba-restored-database" ]; then
  fail "SQL backup and attach restore paths are staged for DBA workflow selection but not automated in this release. For this release, restore the SQL database first, then choose RESTORE_SOURCE=dba-restored-database."
fi

if ! id "${SERVICE_USER}" >/dev/null 2>&1; then
  info "Creating service user ${SERVICE_USER}."
  useradd --system --home "${DATA_DIR}" --shell /usr/sbin/nologin "${SERVICE_USER}"
fi

info "Creating install/config/data/log directories."
install -d -o root -g root -m 755 "${INSTALL_DIR}"
install -d -o root -g root -m 755 "${CONFIG_DIR}"
install -d -o "${SERVICE_USER}" -g "${SERVICE_USER}" -m 750 "${DATA_DIR}" "${LOG_DIR}"
install -d -o root -g root -m 750 "${BACKUP_ROOT}"

BACKUP_DIR=""
if [ "${ACTION}" = "upgrade" ] || [ "${ACTION}" = "repair" ]; then
  INSTALLED_VERSION="unknown"
  if [ -s "${STATE_FILE}" ]; then
    INSTALLED_VERSION=$(python3 - <<PY
import json
try:
    print(json.load(open('${STATE_FILE}')).get('installedVersion','unknown'))
except Exception:
    print('unknown')
PY
)
  fi
  BACKUP_DIR="${BACKUP_ROOT}/${INSTALLED_VERSION}-before-${ARKANUM_PACKAGE_VERSION}-$(date -u +%Y%m%d%H%M%S)"
  info "Existing installation detected. Backing up app/config to ${BACKUP_DIR}."
  install -d -o root -g root -m 750 "${BACKUP_DIR}"
  [ -d "${INSTALL_DIR}" ] && rsync -a "${INSTALL_DIR}/" "${BACKUP_DIR}/app/" || true
  [ -d "${CONFIG_DIR}" ] && rsync -a --exclude 'master-key' "${CONFIG_DIR}/" "${BACKUP_DIR}/config/" || true
fi

info "Stopping arkanum.service if it exists."
systemctl stop arkanum.service >/dev/null 2>&1 || true

info "Copying published Arkanum application from ${PACKAGE_DIR} to ${INSTALL_DIR}."
rsync -a --delete "${PACKAGE_DIR}/" "${INSTALL_DIR}/"
chown -R root:root "${INSTALL_DIR}"
chmod -R go-w "${INSTALL_DIR}"

MASTER_KEY_FILE="${CONFIG_DIR}/master-key"
if [ "${ACTION}" = "restore" ]; then
  info "Restore/recover existing Arkanum instance selected."
  info "Restore source: ${RESTORE_SOURCE}"
  info "Use database already restored by DBA. Installing provided Arkanum Recovery Key at ${MASTER_KEY_FILE}."
  umask 077
  if [ -n "${ARKANUM_RESTORE_KEY_FILE:-}" ]; then
    [ -s "${ARKANUM_RESTORE_KEY_FILE}" ] || fail "ARKANUM_RESTORE_KEY_FILE does not exist or is empty."
    cp "${ARKANUM_RESTORE_KEY_FILE}" "${MASTER_KEY_FILE}"
  else
    printf '%s\n' "${ARKANUM_RESTORE_KEY}" > "${MASTER_KEY_FILE}"
  fi
  if ! grep -q '^ark-recovery-v1\.' "${MASTER_KEY_FILE}"; then
    fail "Provided Recovery Key must start with ark-recovery-v1."
  fi
  info "No new Recovery Key was generated during restore mode."
elif [ ! -s "${MASTER_KEY_FILE}" ]; then
  info "Generating site-wide Arkanum recovery/master key at ${MASTER_KEY_FILE}."
  umask 077
  python3 - <<'PY' > "${MASTER_KEY_FILE}"
import secrets
print('ark-recovery-v1.' + secrets.token_urlsafe(32))
PY
else
  info "Using existing site-wide Arkanum recovery/master key at ${MASTER_KEY_FILE}."
fi
chown root:"${SERVICE_USER}" "${MASTER_KEY_FILE}"
chmod 640 "${MASTER_KEY_FILE}"

ENV_FILE="${CONFIG_DIR}/arkanum.env"
info "Writing systemd environment file at ${ENV_FILE}."
cat > "${ENV_FILE}" <<EOF_ENV
ASPNETCORE_ENVIRONMENT=Production
ARKANUM_MASTER_KEY=$(cat "${MASTER_KEY_FILE}")
ConnectionStrings__DefaultConnection=${ARKANUM_SQL_CONNECTION}
EOF_ENV
chown root:"${SERVICE_USER}" "${ENV_FILE}"
chmod 640 "${ENV_FILE}"

DOTNET_BIN=$(command -v dotnet)
info "Writing systemd unit /etc/systemd/system/arkanum.service."
cat > /etc/systemd/system/arkanum.service <<EOF_SERVICE
[Unit]
Description=Arkanum internal credential vault
After=network-online.target
Wants=network-online.target

[Service]
User=${SERVICE_USER}
Group=${SERVICE_USER}
WorkingDirectory=${INSTALL_DIR}
EnvironmentFile=${ENV_FILE}
ExecStart=${DOTNET_BIN} ${INSTALL_DIR}/Arkanum.Web.dll --urls ${APP_URLS}
Restart=always
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF_SERVICE

info "Writing install state manifest at ${STATE_FILE}."
python3 - <<PY > "${STATE_FILE}"
import json, datetime
state = {
  "product": "Arkanum",
  "installedVersion": "${ARKANUM_PACKAGE_VERSION}",
  "installedAtUtc": datetime.datetime.utcnow().replace(microsecond=0).isoformat() + "Z",
  "installAction": "${ACTION}",
  "installPath": "${INSTALL_DIR}",
  "configPath": "${CONFIG_DIR}",
  "dataPath": "${DATA_DIR}",
  "logPath": "${LOG_DIR}",
  "serviceName": "arkanum.service",
  "databaseProvider": "SqlServer",
  "packageChannel": "prerelease",
  "backupPath": "${BACKUP_DIR}"
}
print(json.dumps(state, indent=2))
PY
chown root:"${SERVICE_USER}" "${STATE_FILE}"
chmod 640 "${STATE_FILE}"

write_report

info "Enabling and restarting arkanum.service."
systemctl daemon-reload
systemctl enable arkanum.service
systemctl restart arkanum.service

HEALTH_URL="${APP_URLS/0.0.0.0/127.0.0.1}/health"
info "Running post-install health check at ${HEALTH_URL}."
set +e
HEALTH_OUTPUT=$(python3 - <<PY 2>&1
import json, urllib.request, sys
url='${HEALTH_URL}'
try:
    with urllib.request.urlopen(url, timeout=20) as response:
        body=response.read().decode('utf-8', 'replace')
    print(body)
    data=json.loads(body)
    sys.exit(0 if str(data.get('status','')).lower() == 'healthy' else 2)
except Exception as exc:
    print(exc)
    sys.exit(3)
PY
)
HEALTH_STATUS=$?
set -e
RECOVERY_VALIDATION="Not applicable"
if [ "${ACTION}" = "restore" ]; then
  if [ "${HEALTH_STATUS}" -eq 0 ]; then
    RECOVERY_VALIDATION="Passed"
  else
    RECOVERY_VALIDATION="Failed"
  fi
fi
{
  echo "Post-install health status code: ${HEALTH_STATUS}"
  echo "${HEALTH_OUTPUT}"
  echo "Recovery sentinel validation: ${RECOVERY_VALIDATION}"
} >> "${REPORT_FILE}"

if [ "${HEALTH_STATUS}" -ne 0 ]; then
  if [ "${ACTION}" = "restore" ]; then
    echo "[FAIL] The provided Recovery Key does not match this database. The provided Arkanum Recovery Key does not match the restored database. Recoverable state requires both the SQL database backup and the matching Arkanum Recovery Key. No new Recovery Key was generated. Report: ${REPORT_FILE}" >&2
    exit 7
  fi
  cat >&2 <<EOF
[WARN] Arkanum installed but health validation did not pass.
Report: ${REPORT_FILE}
Rollback app files from backup if needed:
  ${BACKUP_DIR}
Rollback app files command example:
  sudo rsync -a ${BACKUP_DIR}/app/ ${INSTALL_DIR}/ && sudo systemctl restart arkanum.service
EOF
else
  info "Health validation passed."
fi

cat <<EOF
[OK] Arkanum ${ACTION} completed for version ${ARKANUM_PACKAGE_VERSION}.

Report:
  ${REPORT_FILE}

State manifest:
  ${STATE_FILE}

Next checks:
  sudo systemctl status arkanum.service --no-pager
  APP_URL=${APP_URLS/http:\/\/0.0.0.0/http:\/\/127.0.0.1} installers/linux/verify-arkanum.sh

Important recovery material:
  ${MASTER_KEY_FILE}

Back up the SQL database and this site-wide recovery/master key together. The database alone cannot decrypt existing vault contents after restore.
EOF
